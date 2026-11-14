# Feature: AI-Assisted Week Planning

## November 2026 review followups

Source discovery now preserves engine ranking, verifies the requested host before
deciding whether fallback is needed, and reports a limited metasearch service when
upstream engines fail. Sitemap discovery ranks recipe slugs using source vocabulary,
term rarity and compound segmentation. Known non-recipe source routes are excluded
before preview; actual structured extraction and ingredient assessment still determine
eligibility. Titles and URLs do not establish ingredient facts.

Distinct URLs share IDs and resolution tasks. Research allocates waves fairly across
requirements. The first verified rejections can trigger a bounded `planning.research`
query refinement; an exhausted search can receive one further refinement while
capacity remains, with at most four such calls per session. The strict contract,
shared operation ID, eligibility, settings and outcomes use the central execution gate.
Refinement receives up to three eligible and three rejected assessed examples, with
bounded titles and canonical classes. Titles guide retrieval but cannot prove facts.
When facts reasoning is enabled and only one requirement remains, its remaining
research uses bounded four-page waves rather than a serial one-page tail.

The inspection ceiling is **28 distinct pages**, raised from 24 after a measured
mixed-source failure spent its allowance on 20 main candidates and four dessert
candidates, including a non-recipe page. Research also checks the shared remaining
provider attempts and reserves the configured generation-attempt allowance. The
hard **32-provider-attempt / 300-second** operation defaults remain unchanged.
Transport failures can still consume extra attempts; the gate remains authoritative.

A plan that meets source counts still considers assessed saved alternatives on its
other dates when the household has canonical allergies. This closes an early-return
path that retained unverified plain dishes despite available assessed food. Under the
user's clarified policy, dates without sufficient distinct assessed meals stay empty
and the plan is partial, with localized dated explanations. New generation omits
unassessed dishes when an attendee has canonical allergy exclusions. This preserves
source, count, date, uniqueness and allergy requirements.

Intent v11 retains the date repairs: it reserves explicit dates before assigning undated counts and rejects a
nonempty date list whose distinct-date count differs from the required meal count.
Dates represent mandatory selections rather than an optional scheduling window. An incompatible
interpretation gets a correction within the existing retry allowance; a confirmed
contradictory request is still reported as impossible. The Friday source scenario
now expects a partial week and all four eligible saved controls, following the user's
explicit decision. Its original prompt and hard source/date/allergy assertions remain.

The independently labelled facts corpus and full repeated planning results are
reported separately in [current verification evidence](../reviews/dish-identity-fix-2026-11-14.md).

Specific-food identity now uses the central `recipe.dish_match` task and strict
`recipe-dish-match-v1` contract. Bounded titles, ingredients and preparation establish
the requested food independently of allergy classes. Content-bound approvals are
checked in final selection and acceptance; changed recipe content requires review.
Only eligible, identity-matched external candidates receive verified research scope,
and they cannot leak into other source/count/date clauses. Intent v11 always preserves
the requested food for source constraints. The separate identity corpus passed 69/69
on v4.1-flash, while the planning oracle independently checks labelled recipe URLs.
Rechecking earlier Friday source/date-only successes gives 0/3: those selected unrelated
food. Finding enough eligible source recipes remains a separate retrieval-quality
question; a valid identity check does not guarantee a filled Friday or a complete week.
The user clarified Friday as Belgian/Dutch pancakes only, with an explained empty
date when none qualify. The original fixture prompt remains verbatim; a separate
`instructionClarification` supplies that decision. The runner retains strict meal
completion and separately reports `policyPassed`/`acceptedPartial`, so an approved
gap is visible without accepting unrelated food. Offline oracle checks reject
unexplained gaps, wrong regional styles and allergy violations.

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [meal-feedback.md](meal-feedback.md), [freezy-integration.md](freezy-integration.md)

## Feature Goal

Propose a week of dinners from the household's own data: member constraints/allergies,
favorites, dish history with eaten/rating feedback (variety), vague instructions already
on the plan, and expiring Freezy items. LLM-backed with a deterministic rules fallback,
behind the existing `IMealSuggestionService` seam — AI is not bolted on anywhere else.

## Typed multilingual planning boundary (July 2026)

Free-form instructions are interpreted once into a versioned `PlanningIntent`, regardless
of input language. Each explicit requirement becomes a typed constraint containing its id,
dates, meal type, course, source host, count, distinctness and canonical ingredient classes.
The intent also carries stable required collection ids and separately declared soft
collection preferences. Invalid interpretation returns bounded, structured field errors
to the corrective attempt; missing source queries and dropped collection references do
not silently become an unrestricted plan.
Downstream validation, repair and post-processing do not inspect words in the original
prompt. Final suggestions claim constraints through `constraintIds`.

The bounded domain selector considers proposed rows, all local recipes, resolved external
research candidates and available freezer items against the shared final-item validator.
It checks membership, recipe identity, origin, time, facts, attendees and slot together,
then searches at most 25,000 assignments for counts, uniqueness and freezer limits.
It does not cycle one recipe to fabricate distinct coverage. External research retains
the originating requirement ids, including when one cached query serves multiple courses.
Unknown facts produce a separate unmet-facts diagnostic; repaired model output alone is
not a product-level `model-quality` failure. A partial result is bounded candidate evidence,
not proof that no suitable recipe exists elsewhere.

Source research requests are derived from the typed intent. Search and sitemap results are
only candidates; they count as recipes after a structured import preview succeeds. Candidate
ingredients are assessed into canonical facts before satisfying dietary constraints. Page
classification uses structural URI/import signals, not title words. Unassessed recipes
cannot fill meals for attendees with canonical allergies. Unknown facts remain distinct
from assessed empty classes; no title or ingredient-substring inference is used.

Each persisted planning run includes the normalized intent JSON alongside model, search,
resolution, parse, fallback, token and timing metrics.

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
| `Ai__AgentTimeoutSeconds` | External-research timeout. Research is server-orchestrated rather than an open-ended model tool loop |

Task-specific caps (`FactsMaxOutputTokens`, `TranslationMaxOutputTokens`,
`TranslationMaxPromptTokens`, `ImportExtractionMaxOutputTokens`, and
`ImportExtractionMaxPromptTokens`) keep small classifiers deterministic while allowing
lossless chunking for long translations and bounded source text for recipe extraction.

### Shared AI execution boundary

Week planning, planning-intent extraction, recipe facts, translation, import extraction,
and model capability probes all call `IAiExecutionGate`. The gate resolves the task profile,
applies provider-level reasoning controls, selects JSON Schema only after support is verified,
enforces the task timeout, logs token/finish data, and stores a durable `AiTaskRun`. Feature
services parse and validate their domain result, then mark the completion as successful,
empty, invalid JSON, or domain-invalid. They never construct `ChatOptions` directly.

The Settings → AI task performance page summarizes the retained call window by task and
shows recent call outcomes, latency, P95, token usage, response mode, schema use, task
settings, and contract version. Week planning retains its separate operation-level rows so
search, page resolution, fallback, and model time can still be distinguished.

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
| `WebSearch__MaxResults` | Default 6 — candidate pool per query; research verifies a bounded subset before planning |

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
      ├── MealSuggestionPromptBuilder + MealSuggestionResponseContract
      ├── ExternalRecipeSessionFactory — deterministic search + verified candidates
      ├── MealSuggestionPostProcessor — ids, source scope, freezer and dietary checks
      │   IAiExecutionGate — task profile, schema policy, timeout + call metrics
      └─ on ANY failure → RulesMealSuggestionService (freezer-first + favorite rotation)
```

- **Proposals only**: generation persists nothing. Selected rows are applied through
  `POST /api/plannedmeals/suggestions/accept`, which imports external candidates and creates
  meals with per-item results and idempotency keys. The planner sends its explicit day
  selection as `selectedDates`; those dates become the authoritative `DaysToFill` boundary
  for prompts, deterministic post-processing, rules fallback, and backfill. Without an
  explicit selection (older API callers), `DaysToFill` remains days from today onward
  without a concrete dinner main (vague-only days are included — the suggestion resolves
  the vague text). Suggestions never overwrite concretely planned dishes.
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
  collections) into `CollectionConstraints`: stable manual/auto collection id, full member
  recipe ids, meal/course/date scope, and ≤15 ranked member titles as a prompt sample,
  least-recently-planned first, plus the dates whose instruction referenced it. The
  prompt lists them in their own "Referenced collections" block. The sample bounds
  prompt size; the complete recipe-id membership remains authoritative. Intent v4
  distinguishes mandatory global membership from explicit soft preferences. Dated
  references apply to the exact meal/course slot, and final validation rejects
  same-title impostors and off-list rows. Acceptance resolves required collections by
  stable id and rechecks current membership; removing a member requires another review.
  Unknown, deleted or disabled references return `collection-unresolved` without calling
  the provider. References on unselected days do not constrain the selected dates.
  Renaming a collection still requires updating old raw planning text; a previously
  normalized review requirement survives the rename by id. The inputs get a `#`-triggered autocomplete
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
  `useTools` is true, a small no-reasoning LLM pass translates the fuzzy/multilingual
  instructions into one bounded request per referenced source (query, count, dates and
  course). Ordinary code then searches and verifies the candidate pages before the final
  planner call. SearXNG is tried first; when it has no results, a safe locally ranked source
  sitemap is used. The planner only receives compact, source-labelled verified candidates.
  A deterministic quality gate checks distinct source counts and explicit dates/courses,
  and permits one focused correction pass without re-searching. It also catches obvious
  meat/fish assignments that conflict with attendee exclusions and requests a same-date
  alternative. This replaces the former open-ended model tool loop, which could skip
  research, invent ids, or accumulate tens of thousands of context tokens.
  An external pick comes back to the browser with a server-resolved `sourceUrl` and `SourceName`;
  nothing is imported during generation. **Import happens on accept** through the batch endpoint:
  imports are deduplicated/sequential, freezer availability is rechecked, and a vague idea is removed
  in the same database transaction as its successful replacement. Repeating the same batch+suggestion
  id returns `alreadyApplied` instead of creating a duplicate.
  The external path uses `Ai__AgentTimeoutSeconds`. `Ai__DisableThinking` uses the
  provider-level `reasoning_effort=none` control; a prompt `/no_think` hint was empirically
  ignored by LM Studio. The autocomplete gains an `@`-trigger alongside `#`.
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
  token usage), on research-intent extraction, each source search and recipe resolution
  (with elapsed ms), and at
  every exit (success/malformed/exception/cancelled, each with total elapsed ms) — the tool
  loop interleaves with its own `HttpClient` request logging, so the id is what makes one
  suggestion's full back-and-forth traceable in the merged log stream. `LlmRecipeExtractor`
  and `RecipeImportService.PreviewAsync` (fetch vs. extract broken out separately) log their
  own timings the same way, since they're reachable both from research preview and from a normal
  import. Every raw final model response is logged at `Debug`, correlated by the same request
  id. Container logging defaults to `Information`; set compose interpolation variable
  `LOG_LEVEL=Debug` to expose raw responses or `LOG_LEVEL=Warning` to reduce production logs.
  The Development environment uses `Debug` by default. Each run is persisted with its
  outcome, token totals, parse failures, search/result/resolution counts, fallback count
  and timing breakdown. `GET /api/integrations/ai/planning-runs` powers the technical
  `/settings/ai-stats` page opened by the stats icon beside **AI Week Suggestions**.
- **External candidate provenance**: search results, fetched previews and final selections are
  joined by an opaque candidate id. The model never supplies a URL that the application trusts.
  Structured candidate ingredients also participate in the post-hoc allergy warning net.
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
      constraints drop excluded recipe ids and sampled titles, and the count is surfaced to the review dialog
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
- **The verdict drives behavior.** Planning is eligible only when a response format
  works, the planning example passes, and both English/Dutch intent examples pass.
  A failed or missing quality result produces `failed`; the gate rejects normal intent
  and generation calls. Ordinary suggestions use the rules fallback; uninterpreted
  explicit requirements produce a visible unable result. `warnings` now means the
  required quality checks passed but the model listing disagrees; that advisory listing
  does not disable working completions. The settings page shows format support and
  planning eligibility separately and identifies the checks as finite examples.
  Facts, translation and extraction remain available without planning eligibility;
  their task quality is unverified, and every returned response still needs its own
  contract/domain validation. Failed quality evidence uses the failure cooldown;
  successful quality evidence has the configured expiry.
- **Response-format negotiation** rides on stage 2: a blanket format choice is wrong in
  both directions (LM Studio rejects `json_object`, local reasoning models emit their
  answer into the reasoning channel under a schema — while capable cloud models offer
  guaranteed-valid JSON for free). The test decides empirically per configured model:
verified `json_schema` → production calls set `ChatOptions.ResponseFormat` and the
parse/retry machinery becomes a safety net; otherwise prompted JSON as before.

## Development evaluation corpus

The final plan is checked after normalization, filtering and duplicate removal.
Every selected date needs a dinner main; a dessert does not fill that slot. Claimed
requirements must match the date, meal and course. An exception before instructions
can be interpreted returns an explicit unmet requirement and an unable result.
Contradictory ingredient requirements are preserved by the interpretation prompt and
rejected by the final domain checks. Interpretation quality still depends on the model.

Every provider's suggestions carry resolved attendees and a candidate version.
Review batches persist recipe/facts, attendee restrictions, claimed requirements,
and freezer reservation identity. Acceptance checks that snapshot against current
data and rechecks idempotency inside the stock lock. A changed or legacy candidate
requires another review. External imports remain saved when planning cannot complete.
Required collection memberships are resolved again by stable id; manual membership
rows are locked for the acceptance transaction. Removed membership returns `needsReview`.
The dialog retains failed items for retry, keeps successful items out of the next
submission, and links retained imports that require attention. Finishing a review
with no selected items records rejections without creating meals.

Planning, facts, translation and import extraction share a deadline and total attempt
budget across chunks and corrections. Nested research extraction/facts calls inherit
the planning operation's id, deadline and counter, including parallel work. The gate
claims an attempt immediately before invoking the provider client; a rejected input,
queue cancellation or exhausted budget does not claim one. Capability probes are a
separate shared operation with their own limits and host-lifetime cancellation; the
planning wait still observes the caller's operation deadline.

SDK retries are disabled. The gate retries transient network failures and HTTP
408/429/500/502/503/504 up to `AI_MAX_TRANSPORT_RETRIES` (default one, range zero to
three). It observes `Retry-After` and releases model admission during backoff. A retry
cannot extend the deadline or bypass `AI_MAX_OPERATION_ATTEMPTS`. Semantic corrections
use each task profile's `MaxRetries`; intent no longer owns a separate fixed retry loop.
Every admitted provider invocation gets its own task row and operation-wide ordinal.
`OperationAttempt = 0` means no provider call was admitted; historical rows retain null
and display “not recorded”. `Attempt` remains the feature's completion/correction index.
Rate-limited and attempt-limit outcomes are distinct. Cancelling a caller and reaching
a deadline remain separate terminal outcomes.

The gate uses one conservative input estimate,
including message and schema overhead; translation also reserves retry and output
headroom. `AI_MAX_QUEUED_REQUESTS`, `AI_CONTEXT_WINDOW_TOKENS`,
`AI_OPERATION_TIMEOUT_SECONDS`, and `AI_MAX_OPERATION_ATTEMPTS` configure these
bounds. `ExecuteValidatedAsync` owns response validation and exactly one semantic
outcome; validator exceptions, cancellation and deadline expiry cannot leave a normal
returned response marked as unvalidated. Low-level disposal and durable maintenance
still recover abandoned/interrupted work. Diagnostic-text retention is described in
[deployment security](../deployment-security.md).

The task statistics page cancels an older refresh when a newer selection starts.
Metrics requests omit unset filter parameters, so the initial all-tasks view and
clearing a selected task both load the full retained cohort. HTTP query tests and
a query-sensitive browser journey cover all tasks → one task → all tasks.
Planning metrics record partial/unable outcomes from the final result, and URL import
extraction and enrichment share one operation id per invocation.

Live-model regressions are stored as data in
`src/Dishhive.Api.Tests/Fixtures/AiWeekPlanning/scenarios.json`. Each scenario contains
the prompt, repeat count, advisory performance budget, and hard assertions for distinct
recipes, source hosts, courses, dates, and attendee behavior. A scenario can provide
`selectedDayOffsets`; the runner sends those dates through the product API and fails any
suggestion that escapes the selection. Keep this file as the source of truth so local verification,
developers, and AI-assisted review all evaluate the same cases.
`requiredCollections` asserts actual recipe-id membership fetched from the API, including
automatic collections. Requests start at least seven seconds apart by default to respect
the local ten-per-minute expensive-request limit; `-MinimumRequestIntervalSeconds`
controls that pacing. Report facts readiness and the dataset alongside results: a seeded
recipe with unknown facts, an incompatible assessed recipe and an eligible confirmed
fixture exercise different conditions. Preserve all outcomes instead of weakening assertions.

Run one quick pass against the local Compose app:

```powershell
./scripts/run-ai-week-planning-evals.ps1 -Iterations 1
```

Run a named scenario repeatedly while tuning:

```powershell
./scripts/run-ai-week-planning-evals.ps1 `
  -ScenarioId distinct-vegetarian-source -Iterations 5
```

Scenarios with `status: "known-limitation"` preserve unsupported or unreliable behavior
without turning the normal development suite into an expected-failure test. They are skipped
by default, remain runnable by explicit `-ScenarioId`, and can all be included with
`-IncludeKnownLimitations`.

Deterministic sitemap matching ranks normalized URL tokens, source vocabulary and compound
segments. It does not provide general multilingual spelling correction or transliteration.
The current Friday case is active: typed model interpretation supplies source-language
search wording and ingredient assessment verifies the result. No spelling-specific
production branch was added. Finite corpus success does not guarantee arbitrary wording.

Reports, including raw suggestions and per-iteration failures, are written under the
git-ignored `artifacts/ai-evals/` directory. Accuracy failures return a non-zero exit code;
performance budgets are warnings unless `-FailOnPerformance` is supplied. The scenario
corpus has a normal unit test that validates ids and required expectations without calling
an AI provider. Qualitative AI judging can consume the report later, but it supplements
rather than replaces these deterministic assertions.

The runner checkpoints after every iteration and records transport failures while
continuing the remaining scenarios. `-RequestTimeoutSeconds` defaults to 360, leaving
time for the default 300-second application deadline to return an incomplete result.
The quick-local case checks the returned total time against its 30-minute ceiling;
unknown time is not treated as quick.

## Editable system prompt (July 2026)

The system prompt is split in two (`MealSuggestionPromptBuilder`):

- **`EditableSystemPromptDefault`** — persona and soft preferences (variety, favorites,
  reason style). The user can replace this section from the settings page: persistent
  household guidance ("weekdays max 30 min", "reasons in Dutch") or per-model phrasing
  tuning, which the per-request Instructions field (transient, 500 chars) can't serve.
- **`ProtectedSystemPrompt`** — always appended, never editable: the allergy rule, the
  freezer/collection/source mechanics and the JSON contract. These aren't style — post-
  processing depends on them (stable local recipe ids, freezer item ids and verified
  external candidate ids). Shown read-only in the settings UI so the user sees the full
  picture.
- **Local recipe linking is id-based, not title-based** (July 2026). The known-recipes
  block carries `id`, the current application-language title, and an optional original-
  title alias. Model output copies the id into `localRecipeId`; post-processing resolves
  that id and always restores the current title. Exact current/original title matching is
  retained only as an unambiguous compatibility fallback for older or imperfect model
  output. Linked favorites/history/week-plan rows are canonicalized to the recipe's current
  title before prompting; free-text favorites remain explicitly unlinked.

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
- `components/suggestion-review-dialog/`: after the live AI check, the opening compose
  phase always shows all seven dates. For the current week, today through week-end are
  selected; for a viewed future or past week, selection starts at the first day. Days with
  a concrete dinner main are unselected by default, and every date remains manually
  selectable before the first generation. The picker is hidden during review, and
  Regenerate reuses the original selected set. When AI is reachable
  and its model test has not failed, the optional instructions field shares this same
  phase, so the defaults add no extra click to the AI workflow. When AI is unavailable,
  the same day picker is shown but instructions are hidden because the rules fallback
  ignores them. Then one row per proposal (date, dish, matched-recipe icon, reason, checkbox
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

- **Local reasoning models still need enough input context.** Dishhive disables completion
  reasoning through the provider API by default, independently from `Ai__MaxPromptTokens`
  (which only trims the input recipe/history blocks). The capability test's budget-padded
  evaluation catches an undersized context window on the settings page.
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
- [x] Explicit seven-day generation picker with current-week/planned-meal defaults
- [x] docker-compose `Ai__*` vars + README provider table
- [x] Web search seam (`IWebSearchClient` + SearXNG) + `WebSearch__*` config + integrations status
- [x] Web-search contract health check + actionable settings-page diagnostics for disabled JSON output
- [x] Bounded research-intent extraction + deterministic batched search/preview orchestration
- [x] Source-count/date/course and attendee-assignment validation with focused correction
- [x] Intent v4 collection ids/preferences, bounded field-specific correction errors and unresolved-reference guard
- [x] Shared-invariant candidate selection, exact membership/slot validation and current-membership acceptance recheck
- [x] Mandatory manual/automatic collection corpus cases with recipe-id assertions and paced evaluation requests
- [x] Persisted planning-run metrics + technical settings stats page
- [x] Shared validated execution for every model-backed feature, with separate format and planning-quality eligibility
- [x] Operation-wide deadlines/correlation/provider-attempt counters inherited by nested research, including parallel calls
- [x] Gate-owned observable transport retries, disabled SDK retries, bounded 429 backoff and distinct timeout/cancellation outcomes
- [x] Omit unset metrics filters; HTTP and browser regressions preserve all-task counts through task selection and clearing
- [x] Opaque external candidate ids + strict fetched/source/date provenance enforcement
- [x] Shared bounded public-resource fetcher (redirect validation + public-address connection)
- [x] Idempotent backend suggestion acceptance workflow with per-item outcomes
- [x] Coordinator refactor: prompt, response contract, external session, and post-processor extracted
- [x] `@[Source]` mentions (`SourceMentionResolver`, `RecipeSourceCatalog`, `GET /api/recipes/sources`)
- [x] LLM recipe-extraction fallback for import + `PreviewAsync`; import-on-accept in the review dialog
- [x] docker-compose `searxng` service + `WebSearch__*` vars + `@` autocomplete
- [x] Model capability test: `AiModelTester` (evaluation fixture + scoring) + startup gate + settings-page re-test + response-format negotiation
- [x] Persisted test verdict (`AiModelTestRecord` keyed by config fingerprint) — no re-test per reboot, only on config change or manual re-test
- [x] Editable system prompt (`AiPromptService`, settings card, drift notice, auto re-test on save; protected machinery never editable)
- [x] Id-based freezer linking (`freezerItemId`, id-confirmed dishName override, name-match fallback) replacing exact-dish-name-only matching
- [x] Id-based local recipe linking (`localRecipeId`, current-title canonicalization, unique original-title fallback) replacing exact-title-only matching
