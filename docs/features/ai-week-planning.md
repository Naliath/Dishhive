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
| `Ai__MaxOutputTokens` / `Ai__TimeoutSeconds` | Defaults 8000 / 60 (generous: reasoning models think inside the output budget) |

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
- **Rules fallback**: expiring freezer items (≤10 days past week end) first, then rotate
  favorites — skip dishes planned <14 days ago or rated <3, prefer loved (≥4), round-robin
  across members. Pure function, unit-tested.
- **Failure posture**: AI errors are logged and answered by the fallback; the endpoint
  never 500s because a model is down (Freezy precedent).

## Frontend

- Planner toolbar: `auto_awesome` "Suggest week" button, visible only when the status
  endpoint reports enabled
- `components/suggestion-review-dialog/`: spinner while the model runs, then one row per
  proposal (date, dish, matched-recipe icon, reason, checkbox default-on); "Add selected"
  creates the meals (Dinner/Main, household members attending)

## Risks / Notes

- **Local reasoning models need a large context window.** Qwen3-style models think inside
  the output budget; with LM Studio's default 4096-token context the prompt + reasoning
  exceed the window before any JSON appears and every call lands on the rules fallback.
  Load the model with ≥16k context (`lms load <model> --context-length 16384`). Verified
  June 2026 with `qwen/qwen3.6-35b-a3b` (4096 → always fallback; 16384 → real suggestions).
- Small local models may ignore the JSON schema → malformed-output path lands on the rules
  fallback by design.
- MEAI / Anthropic package APIs still move; versions pinned in the csproj.
- Suggestion quality depends on the configured model; the prompt caps history at 40 dishes
  and recipes at 60 titles to stay within small-model context limits.

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
