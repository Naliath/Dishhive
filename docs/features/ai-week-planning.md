# Feature: AI-Assisted Week Planning

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [meal-feedback.md](meal-feedback.md), [freezy-integration.md](freezy-integration.md)

## Feature Goal

Propose a week of dinners from the household's own data: member constraints/allergies,
favorites, dish history with eaten/rating feedback (variety), vague instructions already
on the plan, and expiring Freezy items. LLM-backed with a deterministic rules fallback,
behind the existing `IMealSuggestionService` seam — AI is not bolted on anywhere else.

## Technology Choice (research summary, June 2026)

| Option | Verdict |
|---|---|
| **Microsoft.Extensions.AI (MEAI)** — provider-agnostic `IChatClient`, .NET 10 ecosystem | ✅ Chosen: exactly fits "one structured completion call behind a seam"; stable; structured output built in |
| Microsoft Agent Framework (successor of Semantic Kernel + AutoGen) | ❌ Multi-agent orchestration in public preview; overkill for a single completion. Builds *on* MEAI, so the upgrade path stays open |
| Semantic Kernel | ❌ More orchestration machinery than needed; Microsoft steers new projects to MAF/MEAI |
| Raw per-provider HTTP clients | ❌ Re-implements what MEAI adapters provide (schema generation, parsing, retries) |

**Provider coverage** — 4 of the 5 providers are OpenAI-compatible, so one factory covers all:

| Provider | SDK | Default endpoint |
|---|---|---|
| OpenAI | OpenAI .NET SDK → `AsIChatClient()` | SDK default |
| Ollama | same | `http://localhost:11434/v1` |
| LM Studio | same | `http://localhost:1234/v1` |
| Mistral | same | `https://api.mistral.ai/v1` |
| Anthropic | official `Anthropic` NuGet (implements `IChatClient`) | SDK default |

## Configuration

`Ai` section in appsettings / `Ai__*` env vars in docker-compose. Disabled while
`Ai:Provider` is empty (Freezy pattern: NoOp service stays registered, UI hides the button).

| Key | Meaning |
|---|---|
| `Ai__Provider` | `openai` \| `anthropic` \| `mistral` \| `ollama` \| `lmstudio` \| `openai-compatible` |
| `Ai__ApiKey` | Falls back to standard `OPENAI_API_KEY` / `ANTHROPIC_API_KEY` / `MISTRAL_API_KEY` env vars; local providers need none |
| `Ai__BaseUrl` | Optional endpoint override (required for `openai-compatible`) |
| `Ai__Model` | e.g. `llama3.1`, `gpt-4o-mini`, `claude-opus-4-8` |
| `Ai__MaxOutputTokens` / `Ai__TimeoutSeconds` | Defaults 12000 / 60 (generous: reasoning models burn 6-8k thinking tokens on instruction-heavy requests before any JSON appears) |
| `Ai__Temperature` | Default 0.3 — low, for steadier structured output and less random regeneration |
| `Ai__MaxRetries` | Default 1 — extra corrective reprompts when a reply can't be parsed before falling back |
| `Ai__MaxPromptTokens` | Default 6000 — rough budget (~4 chars/token) sizing the recipe + history blocks to the model's context window |

## Architecture

```
PlannedMealsController ── POST /api/plannedmeals/suggestions
        │                 GET  /api/plannedmeals/suggestions/status
        ▼
MealSuggestionRequestBuilder (scoped: DbContext + IFreezyClient)
        │  members, favorites, 90d history + ratings, recipes, week plan, DaysToFill
        ▼
IMealSuggestionService
 ├── NoOpMealSuggestionService        (AI unconfigured / Testing)
 └── LlmMealSuggestionService         (AI configured)
      │ IChatClient (ChatClientFactory) — structured JSON output
      └─ on ANY failure → RulesMealSuggestionService (freezer-first + favorite rotation)
```

- **Proposals only**: nothing is persisted; accepted suggestions go through the normal
  `POST /api/plannedmeals`. `DaysToFill` = days without a concrete dinner main (vague-only
  days are included — the suggestion resolves the vague text). Suggestions never overwrite
  concretely planned dishes.
- **Ideas are replaced, not duplicated**: a vague-instruction-only dinner main (an "idea")
  is input for the suggestion, but once an accepted dish lands on that date the planner
  deletes the lingering idea meal — the day ends with the concrete dish only, not both.
- **Free-text instructions**: the request takes optional planner instructions (e.g.
  "3 days vegetarian, at least one fish dish") that the LLM must follow (never overriding
  allergies). The rules fallback ignores them — AI-only by design.
- **#[Collection Name] references** (June 2026, see
  [recipe-organization.md](recipe-organization.md)): day instructions and the global
  instructions may reference a collection — "On friday something from
  #[Easy Weekday Dishes]". `CollectionMentionResolver` resolves them at suggest time
  (grammar `#\[([^\[\]\r\n]{1,100})\]`, name match case-insensitive, manual + auto
  collections) into `CollectionConstraints`: per collection ≤15 member titles,
  least-recently-planned first, plus the dates whose instruction referenced it. The
  prompt lists them in their own "Referenced collections" block (independent of the
  60-recipe cap) with a rule that day-scoped references MUST pick from the list;
  enforcement after parsing is soft (off-list picks are logged, kept for review —
  an empty day would be worse). Dangling references (renamed/deleted collection)
  resolve to nothing and flow through as plain-text hints. The rules fallback honors
  day-scoped constraints (least-recently-planned member, variety window respected,
  after the freezer step) and ignores global ones, consistent with ignoring
  instructions. The inputs get a `#`-triggered autocomplete
  (`directives/collection-mention.directive.ts`): typing `#ea` suggests matching
  collections and inserts the complete token — brackets are never typed by hand.
- **Day adherence & leftovers**: dishes proposed for a day with a vague instruction must
  all satisfy it (a "vegetarian" day gets only vegetarian proposals). Freezer leftovers
  carry their Freezy notes in the prompt (portion hints); the model may propose several
  small leftovers for the same date — post-processing allows up to three distinct dishes
  per day instead of one.
- **Freezer stock**: the prompt only lists freezer items still *available* (Freezy stock
  minus what future meals already reserve — see [freezy-integration.md](freezy-integration.md))
  with their remaining quantity, and the model is told not to exceed it. Post-processing
  links a proposed dish back to the freezer item by name (capped by remaining quantity), so
  accepting it reserves the stock and the same item is never planned into two weeks.
- **Rules fallback**: expiring freezer items (≤10 days past week end) first, then rotate
  favorites — skip dishes planned <14 days ago or rated <3, prefer loved (≥4), round-robin
  across members. Pure function, unit-tested.
- **Robustness posture**: a single unparseable or truncated reply is recovered, not
  discarded. `ParsePayload` strips `<think>`/`<reasoning>` blocks and code fences and
  accepts both the wrapping object and a bare array. On a parse miss the model is
  reprompted once with its bad reply quoted back (`Ai__MaxRetries`); a truncated reply
  (`FinishReason == Length`) is detected and reprompted with a "be concise" nudge. If the
  model still under-delivers — fewer days than asked — the missing days are **backfilled
  from the rules** (those rows are tagged `RulesFallback` so the UI marks them). Only a
  total failure (timeout/HTTP error/exhausted retries) drops to a full rules answer. The
  endpoint never 500s because a model is down (Freezy precedent). Token usage is logged
  per call for tuning.
- **Context budgeting**: recipes are **relevance-ranked** by the request builder
  (favorites, ratings, collection membership; recently-eaten pushed down) rather than sent
  alphabetically, and history is two compact lists (recent-to-avoid, liked/disliked). The
  prompt then trims both to `Ai__MaxPromptTokens`, so the prompt scales with the model's
  context window instead of using fixed caps.
- **Allergy net**: after parsing, a suggestion linked to a known recipe whose ingredient
  names contain a household allergy term gets an `AllergyWarning` (surfaced in the review
  dialog). Heuristic and secondary to the prompt rule — it flags, never drops, and only
  for recipe-linked dishes. The candidate recipes' ingredient names are loaded for this
  check only and are **not** sent to the model.
- **Failure posture**: AI errors are logged and answered by the fallback; the endpoint
  never 500s because a model is down (Freezy precedent).

## Frontend

- Planner toolbar: `auto_awesome` "Suggest week" button, visible only when the status
  endpoint reports enabled
- `components/suggestion-review-dialog/`: a live AI check (integrations status) decides
  the opening phase — AI reachable → instructions are asked **before** generating
  ("compose" phase with a Generate button); AI down → generation starts immediately,
  since the rules fallback ignores instructions. Then one row per proposal (date, dish,
  matched-recipe icon, reason, checkbox default-on); "Add selected" creates the meals
  (Dinner/Main, household members attending). The instructions field is a 3-row textarea
  (so `#[Name]` autocomplete and longer wishes fit); Enter selects the active
  autocomplete option or inserts a newline — it never submits. Generation runs from the
  explicit Generate/Regenerate buttons.

## Risks / Notes

- **Local reasoning models need a large context window.** Qwen3-style models think inside
  the output budget; with LM Studio's default 4096-token context the prompt + reasoning
  exceed the window before any JSON appears and every call lands on the rules fallback.
  Load the model with ≥16k context (`lms load <model> --context-length 16384`). Verified
  June 2026 with `qwen/qwen3.6-35b-a3b` (4096 → always fallback; 16384 → real suggestions).
- Small local models may ignore the JSON schema → malformed-output path lands on the rules
  fallback by design.
- MEAI / Anthropic package APIs still move; versions pinned in the csproj.
- Suggestion quality depends on the configured model; recipes are relevance-ranked and
  the recipe + history blocks are trimmed to `Ai__MaxPromptTokens` (default 6000) so the
  prompt scales with the model's context window instead of fixed 40/60 caps.

## Implementation Checklist

- [x] `AiOptions` (+ standard env-var fallbacks) and `ChatClientFactory` (5 providers)
- [x] Seam hardening: `IsEnabled`, widened `MealSuggestionRequest`
- [x] `MealSuggestionRequestBuilder` (members, favorites, history+ratings, recipes, freezer, DaysToFill)
- [x] `RulesMealSuggestionService` + unit tests
- [x] `LlmMealSuggestionService` (structured output, post-processing, fallback) + unit tests
- [x] Conditional DI in Program.cs (Testing always NoOp)
- [x] Status + suggestions endpoints + integration tests
- [x] Planner button + review dialog + accept flow
- [x] docker-compose `Ai__*` vars + README provider table
