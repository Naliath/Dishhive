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
| `Ai__AgentTimeoutSeconds` / `Ai__MaxToolIterations` | Defaults 300 / 8 — timeout and tool-call cap for the agentic (external-recipe) path only; a tool loop is several full model round-trips, so it needs much more headroom than a plain suggestion call |

### Web search (optional, for external-recipe discovery)

`WebSearch` section / `WebSearch__*` env vars. Disabled while `WebSearch__Provider` is empty
(Freezy pattern: a NoOp client stays registered, the tools are simply not offered) — the
`appsettings.json` default (bare `dotnet run`, no docker). In `docker-compose`, it defaults
to **enabled** against the bundled `searxng` service (scraper-sidecar pattern: the container
is part of the same stack, so it's on unless overridden).

| Key | Meaning |
|---|---|
| `WebSearch__Provider` | `searxng` (self-hosted). Seam is provider-agnostic — Brave/Tavily/... can be added |
| `WebSearch__BaseUrl` | Instance root (e.g. `http://searxng:8080`); JSON output must be enabled on the instance. Host access for local debugging is on `http://localhost:5102` (Dishhive's own `51xx` range, not the collision-prone `8080`/`8888`) |
| `WebSearch__MaxResults` | Default 5 — results returned to the model per query |

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
- **@[Source] external recipe discovery** (July 2026): instructions may reference an
  external website as `@[Source]` ("vegetarian under 30 min from @[Dagelijkse Kost]"). The
  external-recipe tools are gated **exclusively** on this explicit signal —
  `useTools = WebSearch configured && SourceConstraints.Count > 0` — never on instructions
  text alone. An earlier version also triggered on any non-empty instructions ("3 days
  vegetarian" was enough to start a live web search); that's a strictly worse trade for the
  common case, since a tool loop is several full model round-trips (slower, and far more
  tokens — a single observed agentic call ran ~80k tokens vs. a few thousand for a plain
  completion) for something the planner never asked for. The system prompt reinforces the
  same boundary: only the day(s)/wish tied to a `@[Source]` reference may use the tools;
  every other day must come from the known-recipes list or a plain dish name. When
  `useTools` is true, `LlmMealSuggestionService` attaches two read-only tools
  (Microsoft.Extensions.AI `FunctionInvokingChatClient` + `AIFunctionFactory`) and the model
  drives a tool loop:
    - `search_recipes(query, site)` → app-provided web search (`IWebSearchClient`, SearXNG),
      so models without native search can still browse; the site defaults to the referenced
      source's host (`SourceMentionResolver` → `SourceConstraint`, grammar `@\[([^\[\]\r\n]{1,100})\]`,
      resolved via `RecipeSourceCatalog`: dedicated providers + previously-imported hosts, or a
      bare domain typed by hand).
    - `get_recipe(url)` → `RecipeImportService.PreviewAsync`: fetch + structured extract (scraper),
      or the cleaned page text when it can't be parsed, so the model verifies the constraints
      (time, vegetarian, …) before choosing. SSRF-guarded (`UrlGuard`: http/https only, no
      private/loopback hosts).
  An external pick comes back with a `sourceUrl` (and resolved `SourceName`); nothing is imported
  during suggestion. **Import happens on accept**: the review dialog's "Add selected" imports each
  external pick via `POST /api/recipes/import` (dedup by source URL) and then plans it by recipe id
  — proposals-only is preserved. Off-source picks are logged but kept (soft enforcement, like
  collections). Requires a tool-capable model; otherwise it falls back to known-recipe suggestions.
  The agentic path uses `Ai__AgentTimeoutSeconds` and skips the `/no_think` nudge (reasoning helps
  tool use). `ExternalRecipeTools` memoizes `search_recipes`/`get_recipe` per request (keyed by
  query+site / URL) — some models re-issue an identical call, and re-fetching would waste the
  shared time budget on a repeat. The autocomplete gains an `@`-trigger alongside `#` (shared
  directive/util).
- **Day adherence & leftovers**: dishes proposed for a day with a vague instruction must
  all satisfy it (a "vegetarian" day gets only vegetarian proposals). Freezer leftovers
  carry their Freezy notes in the prompt (portion hints); the goal is **enough food for
  the household, not maximum freezer consumption**:
    - **Meals only, never raw ingredients.** A frozen pizza, lasagna, soup, stew, or a
      container of home-made leftovers is a valid dinner pick; a raw ingredient or side
      component (a bag of peas, frozen corn, butter, shredded cheese, flour, …) is not a
      dish and must never be turned into one just because it's expiring — Freezy's item
      model has no meal/ingredient category to check mechanically (see `FrozenItem`/
      `FreezyHttpClient`: name, quantity, unit, expiration, free-text notes only), so this
      is a judgment call only the LLM path can make; the rules fallback (below) can't.
    - **Default portion assumption is household-sized, not partial.** When an item's notes
      give no portion size — the normal case for home-made leftovers in a container — the
      model assumes it already covers the household and proposes it alone. It only reaches
      for a second item on the same date when the notes *explicitly* say the first one's
      portion is smaller than the household (e.g. a frozen pizza noted "for 2" and a
      frozen lasagna noted "for 2" together cover a household of 4). This flips what an
      earlier version did (assume partial by default, look for reasons to combine) — that
      biased toward over-combining; assuming full-size unless told otherwise is the safer
      default and needs actual evidence before padding out a meal with a second item.
    - Each combined dish is still its own **separate suggestion entry** with its exact
      freezer-item name — never merged into one dish name — so `LinkFreezerItems` can
      match each back to its own stock for correct Freezy tracking. Post-processing allows
      up to three distinct dishes per day.
- **Freezer stock**: the prompt only lists freezer items still *available* (Freezy stock
  minus what future meals already reserve — see [freezy-integration.md](freezy-integration.md))
  with their remaining quantity, and the model is told not to exceed it. Post-processing
  links a proposed dish back to the freezer item by name (capped by remaining quantity), so
  accepting it reserves the stock and the same item is never planned into two weeks.
- **Rules fallback**: expiring freezer items (≤10 days past week end) first, then rotate
  favorites — skip dishes planned <14 days ago or rated <3, prefer loved (≥4), round-robin
  across members. Pure function, unit-tested. **Deliberately never combines multiple
  freezer items onto one day** (one item per day, same as everything else this provider
  fills): Freezy notes are free text, not a structured portion size, so this deterministic
  path has no reliable way to judge whether stacking items actually adds up to enough food
  — guessing would risk under- or over-feeding the household. That judgment call needs
  the notes-reading + household-size reasoning only the LLM path can do (see "Day
  adherence & leftovers" above); a July 2026 attempt to have the rules engine pack items
  whenever there were more expiring items than open days was reverted for exactly this
  reason — it optimized for using up stock, not for feeding the household correctly. For
  the same reason it also can't tell a meal-sized freezer item from a raw ingredient
  (Freezy carries no category to check) — a known, accepted gap in this deterministic
  path, not something to paper over with a fragile keyword guess.
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
- **Timing/monitoring** (July 2026): every `SuggestAsync` call gets a short correlation id
  (`[abcd1234]` prefix) logged at start, on each completion attempt (with elapsed ms and
  token usage), on each `search_recipes`/`get_recipe` tool call (with elapsed ms), and at
  every exit (success/malformed/exception/cancelled, each with total elapsed ms) — the tool
  loop interleaves with its own `HttpClient` request logging, so the id is what makes one
  suggestion's full back-and-forth traceable in the merged log stream. `LlmRecipeExtractor`
  and `RecipeImportService.PreviewAsync` (fetch vs. extract broken out separately) log their
  own timings the same way, since they're reachable both from `get_recipe` and from a normal
  import.
- **Context budgeting**: recipes are **relevance-ranked** by the request builder
  (favorites, ratings, collection membership; recently-eaten pushed down) rather than sent
  alphabetically, and history is two compact lists (recent-to-avoid, liked/disliked). The
  prompt then trims both to `Ai__MaxPromptTokens`, so the prompt scales with the model's
  context window instead of using fixed caps.
- **Considered and rejected (July 2026): a `search_known_recipes` tool instead of the
  prompt-injected "Known recipes" block.** The ranking above is already a server-side
  retrieval step (cheap, deterministic, no model round-trip) — the prompt block is its
  *output*, not raw dumped data, and it's capped/trimmed to begin with. Moving it behind a
  tool call would add a full extra model round-trip to every suggestion request (multi-turn
  tool loops run far slower and cost far more tokens than one completion — an observed
  agentic call ran ~80k tokens, dominated by tool-loop history, not the recipe list) and
  force the model to guess a search query without having seen the actual candidates first,
  for negligible token savings given the block is already small. Revisit only if the recipe
  library grows large enough that the ranked/capped list starts meaningfully dropping
  relevant candidates — that's the actual scaling problem a retrieval tool solves.
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
- [x] Web search seam (`IWebSearchClient` + SearXNG) + `WebSearch__*` config + integrations status
- [x] External-recipe tools (`search_recipes`, `get_recipe`) + `FunctionInvokingChatClient` wiring
- [x] `@[Source]` mentions (`SourceMentionResolver`, `RecipeSourceCatalog`, `GET /api/recipes/sources`)
- [x] LLM recipe-extraction fallback for import + `PreviewAsync`; import-on-accept in the review dialog
- [x] docker-compose `searxng` service + `WebSearch__*` vars + `@` autocomplete
