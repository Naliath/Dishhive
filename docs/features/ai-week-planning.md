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

**Provider coverage** — deliberately scoped to OpenAI-compatible APIs only, so one
factory covers every provider (no per-provider SDK/parsing quirks to maintain):

| Provider | SDK | Default endpoint |
|---|---|---|
| OpenAI | OpenAI .NET SDK → `AsIChatClient()` | SDK default |
| Ollama | same | `http://localhost:11434/v1` |
| LM Studio | same | `http://localhost:1234/v1` |
| Mistral | same | `https://api.mistral.ai/v1` |
| openai-compatible (OpenRouter, etc.) | same | requires `Ai__BaseUrl` |

## Configuration

`Ai` section in appsettings / `Ai__*` env vars in docker-compose. Disabled while
`Ai:Provider` is empty (Freezy pattern: NoOp service stays registered, UI hides the button).

| Key | Meaning |
|---|---|
| `Ai__Provider` | `openai` \| `mistral` \| `ollama` \| `lmstudio` \| `openai-compatible` |
| `Ai__ApiKey` | Falls back to standard `OPENAI_API_KEY` / `MISTRAL_API_KEY` env vars; local providers need none |
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
    - Each combined dish is still its own **separate suggestion entry**, linked to its
      own freezer item, so `LinkFreezerItems` can reserve each one's stock independently
      for correct Freezy tracking. Post-processing allows up to three distinct dishes per
      day.
- **Freezer stock**: the prompt only lists freezer items still *available* (Freezy stock
  minus what future meals already reserve — see [freezy-integration.md](freezy-integration.md))
  with their remaining quantity, and the model is told not to exceed it.
- **Freezer linking is id-based, not name-matched** (July 2026): each freezer item in
  the prompt carries its Freezy id (`- id={Id}: {Name} (...)`, the same id
  `FreezerAvailabilityService`/`PlannedMeal.FreezyItemRef` already use to track
  reservations and prevent double-consumption). The model copies that id verbatim into
  `freezerItemId` on the suggestion; `PostProcess` resolves it and **overrides
  `dishName` with the item's real name**, so a paraphrase like "leftover lasagna,
  serves 2" still links correctly and the app no longer depends on the model
  reproducing a (possibly long) item name character-for-character. This also lowers
  output tokens: `dishName` for a freezer pick can be a short label since the app
  supplies the authoritative name. An id the model invents or that's gone stale
  (reserved elsewhere between prompt build and reply) is logged and ignored, not
  fatal — the dish falls through to `LinkFreezerItems`' name-exact-match fallback
  (kept for models that ignore `freezerItemId` entirely, e.g. weaker prompted-JSON
  models) and, failing that, is kept as a valid but unlinked suggestion rather than
  dropped. Quantity capping (never reserve more than remains) applies the same way
  regardless of which path confirmed the link; an id-confirmed suggestion that loses
  a cap race is unlinked, not discarded.
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
- **Constraint net & allergy pre-filter** (July 2026, see
  [dietary-facts.md](dietary-facts.md)): recipes carry assessed ingredient-class facts
  and member tags carry per-member excluded classes, so the checks are exact where
  facts exist:
    - **Pre-filter**: `MealSuggestionRequestBuilder` computes `AllergyExcludedRecipeIds`
      (assessed facts ∩ an attending member's allergy exclusions). `KnownRecipes` stays
      complete so titles remain resolvable — the prompt omits excluded recipes from its
      block, the rules fallback skips favorites matching them entirely, `#[Collection]`
      constraints drop excluded titles, and the count is surfaced to the review dialog
      (`excludedForAllergies`) so a false-positive AI fact is discoverable. Diets never
      hard-filter — annotation + warning only.
    - **Prompt annotations**: assessed candidates render `[contains: Milk, Gluten]`
      (`[contains: none]` = verified clean), member tags render `[excludes: …]`, and a
      protected-prompt rule forbids overlap — covering the free-text dishes the model
      invents, which no list filter can.
    - **Post-hoc net** (`FlagConstraintConflicts`): assessed + linked → exact
      intersection (allergy ⇒ `AllergyWarning`, diet ⇒ `DietWarning` — diets previously
      had no post-hoc check at all); unassessed + linked → the legacy ingredient-name
      substring heuristic, allergies only. Flags, never drops. Ingredient names are
      still loaded for the heuristic only and are **not** sent to the model.
    - **Coverage fix (was a real hole)**: flagging used to run only inside
      `PostProcess`, so full rules-fallback answers (capability gate, unparseable
      reply, exception) and rules-backfilled days were returned with checkable allergy
      conflicts **unflagged**. `Finalize()` now runs the net as the single last step of
      every `SuggestAsync` exit; regression-tested.
- **Failure posture**: AI errors are logged and answered by the fallback; the endpoint
  never 500s because a model is down (Freezy precedent).

## Model capability test (July 2026)

"Reachable" says nothing about "usable": the `/models` ping passes while the loaded
model may still produce no parseable JSON at all (the 4k-context reasoning-model trap)
or quietly ignore instructions. Every suggestion against such a model is a known-doomed
wait ending in silent rules fallback, rediscovered on every click. So the configured
model gets **one real test per process** (`AiModelTester` + `AiModelCapabilityService`):

- **Startup**: `AiModelStartupTest` (hosted service) resolves the verdict in the
  background. The application stays fully usable while it runs — only AI work waits:
  `LlmMealSuggestionService` awaits the shared verdict before its first model call.
- **Persisted per configuration**: the verdict is stored (`AiModelTestRecord`, unique
  per `AiOptions.CapabilityFingerprint` — provider, model, base URL and the
  capability-relevant tuning knobs; operational timeouts excluded). A restart with
  unchanged AI settings reuses the stored verdict instantly; a fresh test only runs
  when the fingerprint has no stored row yet, i.e. when the AI config changed.
  Known trade-off: a server-side change behind identical settings (a different model
  loaded into LM Studio under the same id, a changed context length) is **not**
  auto-detected — that is exactly what the re-test button is for.
- **Re-trigger**: the settings page (integrations card) shows the verdict, the
  per-check details and a "(Re-)test model" button — `POST /api/integrations/ai/test`,
  poll the GET until `completed` — for after the user swapped or reconfigured the
  model (it always runs and overwrites the stored verdict).
- **What it verifies**:
  1. `/models` probe — endpoint up and the configured model id actually served
     (catches "wrong model loaded"). Advisory only: some gateways block the listing
     while completions work, so this never aborts the real stages.
  2. A **medium-complexity evaluation request** under the production system prompt and
     prompt builder, padded with filler recipes to the full `Ai__MaxPromptTokens`
     budget — a context window too small for real requests fails *here*, visibly,
     instead of on every planning evening. Sent first with native `json_schema`
     enforcement, then with prompted JSON; whichever parses becomes the verified
     response mode.
  3. The reply is **scored against known-correct answers**: all five days filled, the
     `#[Quick Pasta]`-constrained Wednesday picks from that collection, "serve Chicken
     curry on Thursday" is honored, and ≥2 days are vegetarian. The fixture recipes
     carry Vegetarian/Meat/Fish categories, so this tests instruction-following, not
     world knowledge.
- **The verdict drives behavior.** `failed` (no attempt produced parseable JSON): the
  LLM is never called — suggestions go straight to the rules, and the review dialog
  skips the compose phase exactly like "AI down", so instructions are never collected
  just to be silently dropped. `warnings` (JSON works but the evaluation or the
  model-listing check failed): the AI path stays on; the settings page shows what went
  wrong so expectations are set. `passed`: business as usual.
- **Response-format negotiation** rides on stage 2: a blanket format choice is wrong in
  both directions (LM Studio rejects `json_object`, local reasoning models emit their
  answer into the reasoning channel under a schema — while capable cloud models offer
  guaranteed-valid JSON for free). The test decides empirically per configured model:
  verified `json_schema` → production calls set `ChatOptions.ResponseFormat` and the
  parse/retry machinery becomes a safety net; otherwise prompted JSON as before.

## Editable system prompt (July 2026)

The system prompt is split in two (`LlmMealSuggestionService`):

- **`EditableSystemPromptDefault`** — persona and soft preferences (variety, favorites,
  reason style). The user can replace this section from the settings page: persistent
  household guidance ("weekdays max 30 min", "reasons in Dutch") or per-model phrasing
  tuning, which the per-request Instructions field (transient, 500 chars) can't serve.
- **`ProtectedSystemPrompt`** — always appended, never editable: the allergy rule, the
  freezer/collection/source mechanics and the JSON contract. These aren't style — post-
  processing depends on them (exact recipeTitle and freezer-name matching, the sourceUrl
  contract). Shown read-only in the settings UI so the user sees the full picture.

Design notes (deliberate trade-offs, discussed before building):
- **Textual protection is not behavioral protection.** A conflicting editable section can
  still talk the model out of the protected rules; the real guards are post-processing,
  the rules fallback, and the capability test. That's why **saving a changed prompt
  automatically restarts the model capability test** — the verdict shows whether the
  custom prompt still yields working suggestions (a heavily opinionated prompt, e.g.
  "strictly vegan", can legitimately fail the instruction-following evaluation checks).
- **Customizers fork off the improvement train.** The stored override freezes the user on
  their text while the shipped default keeps improving. `AiPromptService` stores the
  default-at-customization-time alongside the override; the settings card shows a
  "built-in prompt improved since you customized" notice so the fork is at least visible.
  Saving text identical to the default clears the customization instead of storing it.
- The capability-test fingerprint includes a hash of the **full effective prompt**, so a
  user edit *and* a shipped-prompt change in an app update both invalidate the persisted
  verdict; a prompt edited while a test is mid-run is handled by re-running against the
  latest prompt before the shared task completes.

Storage: `UserSettings` rows (`aiSystemPrompt`, `aiSystemPromptBaseline`; Value widened
to 4000 chars). API: `GET/PUT/DELETE /api/settings/ai-prompt`.

## Frontend

- Planner toolbar: `auto_awesome` "Suggest week" button, visible only when the status
  endpoint reports enabled
- `components/suggestion-review-dialog/`: a live AI check (integrations status) decides
  the opening phase — AI reachable **and** the model test not failed → instructions are
  asked **before** generating ("compose" phase with a Generate button); AI down or model
  unfit → generation starts immediately, since the rules fallback ignores instructions.
  Then one row per proposal (date, dish, matched-recipe icon, reason, checkbox
  default-on); "Add selected" creates the meals (Dinner/Main, household members
  attending). The instructions field is a 3-row textarea (so `#[Name]` autocomplete and
  longer wishes fit); Enter selects the active autocomplete option or inserts a newline
  — it never submits. Generation runs from the explicit Generate/Regenerate buttons.
  When **every** returned row is fallback-sourced, a banner says so explicitly (and that
  any typed instructions were not applied) — the per-row "rules" tags alone are easy to
  miss.
- `components/integrations-status/` (settings page): under the AI row, the last model
  test verdict with its per-check list and a "(Re-)test model" button; while a test
  runs, the cooking-pot loader with a "can take a couple of minutes" note (polls the
  test endpoint every 2s) and the button is hidden until it completes. The Web Search
  row probes the actual JSON search contract rather than only service availability;
  an instance that rejects `format=json` is marked **Misconfigured** with the required
  nested `search: formats:` SearXNG setting shown inline.
- `components/ai-prompt-settings/` (settings page, shown only when AI is configured):
  the editable prompt section (monospace textarea, 4000-char cap) with Save /
  Reset-to-default, a "customized" tag, the drift notice, and the protected rules in a
  collapsed read-only panel. Saving points the user at the restarted model test.

## Risks / Notes

- **Local reasoning models need a large context window.** Qwen3-style models think inside
  the output budget; with LM Studio's default 4096-token context the prompt + reasoning
  exceed the window before any JSON appears and every call lands on the rules fallback.
  Load the model with ≥16k context (`lms load <model> --context-length 16384`). Verified
  June 2026 with `qwen/qwen3.6-35b-a3b` (4096 → always fallback; 16384 → real suggestions).
  The capability test's budget-padded evaluation catches this on the settings page
  instead of in the logs.
- Small local models may ignore the JSON shape → malformed-output path lands on the rules
  fallback by design; a model that never produces JSON fails the capability test and is
  not called at all. Local models being unfit is an accepted outcome — the point is that
  it is now communicated (settings verdict, dialog behavior), not discovered per request.
- MEAI / OpenAI SDK APIs still move; versions pinned in the csproj.
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
- [x] Web-search contract health check + actionable settings-page diagnostics for disabled JSON output
- [x] External-recipe tools (`search_recipes`, `get_recipe`) + `FunctionInvokingChatClient` wiring
- [x] `@[Source]` mentions (`SourceMentionResolver`, `RecipeSourceCatalog`, `GET /api/recipes/sources`)
- [x] LLM recipe-extraction fallback for import + `PreviewAsync`; import-on-accept in the review dialog
- [x] docker-compose `searxng` service + `WebSearch__*` vars + `@` autocomplete
- [x] Model capability test: `AiModelTester` (evaluation fixture + scoring) + startup gate + settings-page re-test + response-format negotiation
- [x] Persisted test verdict (`AiModelTestRecord` keyed by config fingerprint) — no re-test per reboot, only on config change or manual re-test
- [x] Editable system prompt (`AiPromptService`, settings card, drift notice, auto re-test on save; protected machinery never editable)
- [x] Id-based freezer linking (`freezerItemId`, id-confirmed dishName override, name-match fallback) replacing exact-dish-name-only matching
