# AI Agents (Meal Planning + Adaptive Recipe Import)

## Summary

Two assistive capabilities powered by an LLM through the **Microsoft Agent Framework**
(`Microsoft.Agents.AI`) on top of the **Microsoft.Extensions.AI** abstractions:

1. **Meal Planning Agent** — suggests recipes for week-plan slots (single-shot or
   conversational) using the household's recipes, family preferences, recent dish
   history, and available frozen items.
2. **Adaptive Recipe Import Agent** — fallback for recipe URLs from sites without a
   hand-written `IRecipeSourceProvider`. The agent extracts the recipe **and** emits a
   reusable parsing **blueprint** persisted to the database. Subsequent imports from
   the same host are handled by a static provider that replays the blueprint without
   any LLM call — making the LLM a one-time teacher per source.

These features are **opt-in**: when the AI provider in configuration is `disabled`
(default), every existing endpoint behaves exactly as today. Integration tests use a
fake `IChatClient` so no network or API key is ever required to build, test, or ship.

## Goals

- Use the official Microsoft Agent Framework — `ChatClientAgent`, `AIAgent`, `AIFunctionFactory` — for the conversational/tool-calling surface.
- Keep all LLM access behind a single, replaceable seam (`IChatClient`).
- Never call out to a real LLM in unit/integration tests (fake `IChatClient`).
- Allow the recipe-import agent to "graduate" a new source into a static provider so
  cost and latency are paid at most once per host.
- Avoid runtime C# code generation (security & complexity); blueprints are declarative JSON.

## Non-goals (v1)

- Vector search / RAG over recipes (search uses simple `Contains`; that's enough for ~hundreds of recipes).
- Multi-turn long-running planning ("plan the next 4 weeks balancing macros") — only single-week suggestions.
- Cost/quota management UI.
- Anti-bot evasion — if a site rejects our fetch, we surface the failure verbatim.

## Configuration

```jsonc
"Dishhive": {
  "Ai": {
    "Provider": "disabled",        // "openai" | "ollama" | "disabled"
    "Model": "gpt-4o-mini",
    "Endpoint": null,              // optional override (Ollama / Azure OpenAI gateway)
    "ApiKey": null,                // pulled from env var DISHHIVE__AI__APIKEY in deployment
    "RequestTimeoutSeconds": 60,
    "RecipeImport": {
      "MaxHtmlChars": 60000        // truncate fetched HTML before sending to LLM
    }
  }
}
```

When `Provider` is `disabled`, `IChatClient` is registered as a `DisabledChatClient`
that throws `AgentUnavailableException`. All agent endpoints check `IsAvailable`
first and return `503 Service Unavailable` with a friendly error.

## Data model additions

Single new entity `LearnedRecipeSource` (table: `LearnedRecipeSource`):

| Column | Type | Notes |
|---|---|---|
| `Id` | `uuid` | PK |
| `Host` | `text` | unique; lowercase host (`example.com`) |
| `ProviderKey` | `text` | derived (`learned:example.com`) |
| `Strategy` | `text` | `JsonLd` or `XPath` |
| `BlueprintJson` | `jsonb` | strategy-specific selectors |
| `LearnedAt` | `timestamptz` | |
| `LastUsedAt` | `timestamptz?` | updated on every successful parse |
| `UseCount` | `int` | incremented per successful parse |
| `SourceUrl` | `text` | originating URL (audit trail) |

`BlueprintJson` shape (versioned with `version: 1`):

- **JsonLd** — `{ "version": 1, "strategy": "JsonLd" }`
  Used when the page has a `schema.org` Recipe in `<script type="application/ld+json">`. The same parser `DagelijkseKostRecipeProvider` uses is reused.
- **XPath** — `{ "version": 1, "strategy": "XPath", "title": "...", "description": "...", "image": "...", "ingredients": "...", "steps": "...", "servings": "..." }`. Each value is an XPath that resolves to one or many nodes (HtmlAgilityPack `SelectNodes`).

Future strategies (`Microdata`, `Rdfa`, `CssSelector`) are additive — the playback provider switches on `Strategy`.

## Backend

### Modules

```
Services/Agents/
  AiAgentOptions.cs               # IOptions-bound config
  IChatClientFactory.cs           # creates IChatClient or DisabledChatClient
  AgentExceptions.cs              # AgentUnavailableException
  Planning/
    IMealPlanningAgent.cs
    MealPlanningAgent.cs          # ChatClientAgent + tools
    MealPlanningTools.cs          # AIFunctionFactory.Create(...) wrappers
    AgentMealSuggestionStrategy.cs# implements IMealSuggestionStrategy
  RecipeImport/
    IRecipeImportAgent.cs
    RecipeImportAgent.cs          # structured-output extraction + blueprint
    LearnedRecipeSourceProvider.cs# IRecipeSourceProvider playback layer
    ILearnedSourceStore.cs        # repository wrapper
    LearnedSourceStore.cs
    JsonLdRecipeParser.cs         # extracted from DagelijkseKostRecipeProvider
    XPathRecipeParser.cs
```

### Endpoints

| Method | Path | Body / params | Returns |
|---|---|---|---|
| `GET` | `/api/agents/status` | — | `{ available, provider, model }` |
| `POST` | `/api/agents/meal-planning/suggest` | `MealSuggestionRequestDto` | `MealSuggestionDto` |
| `POST` | `/api/agents/meal-planning/chat` | `{ messages: [...] }` | `{ reply: "..." }` |
| `GET` | `/api/agents/learned-sources` | — | `LearnedRecipeSourceDto[]` |
| `DELETE` | `/api/agents/learned-sources/{host}` | — | 204 |

`POST /api/recipe-import/preview` is **updated** to:
1. Try static providers (`RecipeSourceRegistry`).
2. Try `LearnedRecipeSourceProvider`.
3. If both fail and AI is available, escalate to `RecipeImportAgent`. The agent persists a blueprint and returns the imported recipe.
4. If AI is unavailable, return the existing 400 error.

### Agent: meal planning (tools)

Tools registered with `AIFunctionFactory.Create(...)`:

- `list_family_members()` → name + preference summary
- `list_recipes(query?, tag?)` → `{ id, title, tags }[]`
- `get_recent_history(days = 14)` → list of `{ date, dishLabel }`
- `get_freezy_items()` → list of frozen items (only when Freezy enabled)
- `get_week_plan(weekStart)` → current planned slots (so the agent doesn't suggest a duplicate)

The agent's instructions:
> "You are Dishhive's meal planning assistant. You suggest one concrete recipe (or vague intent) for a single meal slot. Prefer recipes the family hasn't had in the last 7 days. Honor allergies as hard constraints. Respect the IntentTag if provided. Output JSON: `{ recipeId?, dishLabel, reason }`."

Single-shot path uses `chatClient.GetResponseAsync<MealSuggestionDto>(...)` for typed response.

### Agent: recipe import

Two-phase prompt with structured output:

1. **Phase 1 — fetch & summarize**: HTTP fetch the URL with the same import HttpClient (UA + timeout from config). HTML is **truncated** (config: `MaxHtmlChars`, default 60 000) and stripped of `<script>`/`<style>` content above the body to keep tokens manageable.
2. **Phase 2 — structured extraction**: prompt the model with the cleaned HTML and request a single JSON object matching `AgentRecipeExtraction` (`ImportedRecipe` shape + `Blueprint`). The model is told to:
   - Detect JSON-LD; if present, set `blueprint.strategy = "JsonLd"`.
   - Otherwise, return XPath selectors for `title`, `image`, `description`, `ingredients` (multi-node), `steps` (multi-node), `servings`.
3. **Validation**: replay the blueprint against the fetched HTML. Only persist it if the replay produces a non-empty title and at least one ingredient. (Fail-closed: if validation fails, the recipe is still returned but no blueprint is saved.)

### Wiring into existing recipe import

`RecipeImportService.PreviewAsync` order:
```
TryStaticProvider() ?? TryLearnedProvider() ?? (await _agent.ImportAsync(url, ct))
```

The learned provider is registered as **the last** `IRecipeSourceProvider` in DI (after all static providers), so `RecipeSourceRegistry.FindFor(url)` naturally picks any matching static provider first.

## Frontend

- `recipe-import` page surfaces a small "✨ Used adaptive AI import" badge when the response indicates the agent ran.
- `settings` page gains a section for AI: status (available/unavailable), provider, learned-source list with delete buttons.
- `planner` page gains a "Suggest" button per slot that opens a small sheet with the agent's suggestion + an "Apply" action.

## Risks / unknowns

- **Hallucinated blueprints**: model returns selectors that don't actually match anything. Mitigated by the validation replay above.
- **Cost & rate limits**: only the first import per host pays the LLM cost; meal planning is one call per click.
- **HTML truncation**: long pages may have the recipe near the bottom. Mitigated by stripping scripts/styles and keeping JSON-LD blocks if present (they're cheap to detect with a regex).
- **Provider availability**: if the chosen provider is unreachable at runtime, agent endpoints return 503; planner still works manually.

## Phased plan

1. Add packages, `IChatClient` factory, `AiAgentOptions`. Disabled-by-default. Status endpoint.
2. `LearnedRecipeSource` entity + DbContext mapping + repository.
3. `JsonLdRecipeParser` extraction from DagelijkseKost provider.
4. `LearnedRecipeSourceProvider` playback (no LLM).
5. `RecipeImportAgent` + escalation in `RecipeImportService`.
6. `MealPlanningAgent` + tools + `AgentMealSuggestionStrategy`.
7. `AgentsController` + DTOs.
8. Frontend: agent status check, suggest button, learned-sources management.
9. Tests with `FakeChatClient`.

## Implementation checklist

- [x] NuGet: `Microsoft.Extensions.AI`, `Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI`
- [x] `AiAgentOptions` + `appsettings.json` defaults
- [x] `IChatClientFactory` + `DisabledChatClient` fallback
- [x] `LearnedRecipeSource` entity + DbContext
- [x] `JsonLdRecipeParser` extracted, reused by DagelijkseKost
- [x] `LearnedRecipeSourceProvider` (DB-driven `IRecipeSourceProvider`)
- [x] `IRecipeImportAgent` + structured-output extraction + blueprint validation
- [x] `IMealPlanningAgent` + tool registration
- [x] `AgentMealSuggestionStrategy` replaces `NoopMealSuggestionStrategy` when AI is available
- [x] `AgentsController` + DTOs
- [x] Integration tests with `FakeChatClient`
- [x] Frontend: status check, learned-sources list, agent-aware import UI

