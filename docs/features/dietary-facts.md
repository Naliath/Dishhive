# Feature: Recipe Dietary Facts

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [dietary-tags.md](dietary-tags.md), [ai-week-planning.md](ai-week-planning.md),
[recipe-store.md](recipe-store.md), [week-planner.md](week-planner.md)

## Feature Goal

Give recipes machine-checkable dietary metadata so the week planner can keep
allergy-conflicting recipes out of suggestions and warn about diet conflicts —
deterministically, instead of substring heuristics and LLM title-guessing.

The core split (July 2026 design discussion):

- **Recipes store objective facts** — which canonical *ingredient classes* the dish
  CONTAINS (milk, gluten, pork, …). Facts are AI-detected from the ingredient list at
  add/import time and user-editable afterwards.
- **Members store policies** — each member↔tag link carries its own *excluded classes*
  set (see [dietary-tags.md](dietary-tags.md)). "Vegetarian" is deliberately never a
  recipe property: one member's vegetarian eats fish, another's doesn't, so a
  recipe-side verdict would bake in one definition and be wrong for someone else.
- Matching everywhere is an exact set intersection: `recipe.Contains ∩ member.Excluded`.

## Taxonomy

`IngredientClass` (int enum, persisted by name in CSV columns and by int in fact rows —
never renumber/rename):

- **EU-14 allergens:** Gluten, Crustaceans, Eggs, Fish, Peanuts, Soybeans, Milk,
  TreeNuts, Celery, Mustard, Sesame, Sulphites, Lupin, Molluscs
- **Meat split** (so "no pork" and "no meat but poultry is fine" are expressible):
  RedMeat (beef/veal/lamb/game), Poultry, Pork
- **Diet-relevant extras:** Gelatin, Alcohol, Honey

Sourcing/certification diets (kosher, the slaughter side of halal) are **not**
expressible as ingredient classes; such tags keep an empty machine set and stay
prompt-only, visibly marked in the family form.

## Model

- **`RecipeDietaryFact`** — (RecipeId, IngredientClass) composite-key rows, replaced
  wholesale on each (re)assessment or user edit.
- **`Recipe.DietaryFactsStatus`** distinguishes `Unassessed`, `AiDetected`,
  `UserConfirmed`, `Stale`, `Pending`, and `Failed`. An empty unassessed set means
  unknown. An empty current assessed set means no classified ingredients were found.
  `Revision`, `DietaryFactsAssessedRevision`, `FactsConfirmationVersion`, and
  `DietaryFactsAssessedAt` identify the source and confirmation used by an assessment.
  Previous assessments remain in history after invalidation.
- Exact matching uses only current assessed facts. Source ingredient changes invalidate
  facts immediately, including when AI is unavailable. Reconfirming unchanged classes
  is an explicit user action; human confirmation and source revisions both participate
  in the database concurrency condition.

## AI assessment pipeline

- **`LlmRecipeFactsExtractor`** (`Services/Facts/`) uses the shared AI execution gate
  with task settings, a shared operation deadline and an attempt budget. It is a NoOp
  when AI is unconfigured and does not require the planning capability test. The prompt
  asks for factual containment only — never diet verdicts — and is deliberately biased
  to over-include on uncertain processed products: a false "contains" surfaces visibly
  (warning/exclusion the user can correct), a missed allergen does not. Derived
  ingredients are called out (boter→Milk, sojasaus→Soybeans+Gluten, vissaus→Fish, …).
- **`RecipeFactsAssessmentService`** stores facts and translation jobs separately in
  PostgreSQL. Recipe mutations and their jobs commit together; a bounded channel only
  wakes the worker. Pending work coalesces by recipe and task. Claims and completions
  compare state, lease ownership and captured revisions atomically. Every poll recovers
  expired leases, including leases that expire after a restart. Results computed from
  an old source or confirmation are superseded. Translation checks the recipe revision
  at commit even when only ingredients or steps change. Failed work retries with backoff,
  then becomes visibly failed. Recipes without
  ingredients cannot be classified from their title alone.
- **Hooks:** URL import + re-import (`RecipeImportService`), manual create, update
  with a changed ingredient list (`RecipesController`), file import for recipes
  arriving without facts (`RecipeExchangeService`).
- **Backfill** (user decision: manual, only unassessed, visible progress): the
  settings-page card shows unassessed/AI-detected/confirmed counts and an "Assess N
  unclassified recipes" button → `POST /api/recipes/facts/backfill`; while the queue
  runs the card polls `GET /api/recipes/facts/status` (2s, integrations-status
  pattern) and shows the remaining count.

## API

- `RecipeDto.dietaryFacts` includes `contains`, `status`, `assessedAt`, `recipeRevision`,
  `assessedRevision`, `isCurrent`, and history. Create/update accept optional
  `containsClasses`; source edits invalidate the old assessment unless explicitly
  reconfirmed. A supplied list records user-confirmed facts for the current source.
- `PUT /api/recipes/{id}/facts` `{ contains: [] }` → `UserConfirmed` (detail-page editor).
- `GET /api/recipes/facts/status`, `POST /api/recipes/facts/backfill`.
- `GET /api/dietarytags/preset?name=&kind=` — preset preview for the family form.
- Export/import: assessed facts travel as `dishhive:dietaryFacts` `{ status, contains }`
  (status included so a user-confirmed verdict survives the round trip); recipes
  imported without facts go to the AI queue.

## Planner integration (see [ai-week-planning.md](ai-week-planning.md))

- **Allergy pre-filter** (user decision: any assessed facts filter, diets never do):
  the request builder computes `AllergyExcludedRecipeIds` (assessed ∧ contains ∩ any
  attending member's allergy exclusions). `KnownRecipes` stays complete — consumers
  filter by id, so titles remain resolvable:
  - the prompt omits excluded recipes from its known-recipes block,
  - the rules fallback skips favorites matching them entirely (proposing the dish
    name unlinked would dodge the exclusion, not honor it),
  - `#[Collection]` constraints drop excluded titles at build time,
  - the count is surfaced (`excludedForAllergies`) as a review-dialog notice, so a
    false-positive AI fact is discoverable instead of a recipe silently never
    appearing again.
- **Prompt annotations:** assessed candidates carry `[contains: Milk, Gluten]`
  (`[contains: none]` = verified clean); member tags carry `[excludes: …]`; the
  protected system prompt explains the classes and forbids overlap — free-text dishes
  the model invents are still its responsibility.
- **Post-hoc net** (`FlagConstraintConflicts`, replacing `FlagAllergyConflicts`):
  - assessed + linked → exact intersection: allergy tags ⇒ `AllergyWarning`, diet
    tags ⇒ new `DietWarning` (diets previously had no post-hoc check at all);
  - unassessed + linked → the legacy ingredient-substring heuristic (allergies only;
    language-fragile — "Ei" matches "prei" — but better than nothing until assessed);
  - flags, never drops (unchanged posture).
- **Coverage fix (was a real hole):** flagging used to run only inside `PostProcess`,
  so full rules-fallback answers (capability gate / unparseable reply / exception)
  and rules-backfilled days were returned **unflagged**. `Finalize()` now runs the
  net as the single last step of every `SuggestAsync` exit path; regression-tested.
- The capability-test fixture recipes carry assessed facts so the evaluation prompt
  uses the production annotation format (the system-prompt change also invalidates
  stored verdicts → automatic re-test, by design).

## Frontend

- **Class picker** (`components/class-picker/`) — grouped checkboxes (EU-14 / Meat /
  Other), shared by the family form and the recipe form/detail.
- **Family page** — tag chips open a per-member definition editor (preset-seeded via
  the preset endpoint, so it previews before saving); unmapped tags show a
  "not machine-checkable" hint.
- **Recipe form** — "Contains" card; only submitted when touched (untouched keeps AI
  detection/stored facts).
- **Recipe detail** — facts chips + status badge (AI-detected / Confirmed / Not
  assessed), one-click **Confirm** and inline **Edit**. Import lands here, so the
  badge is the review-what-AI-detected surface.
- **Review dialog** — `dietWarning` per row (tertiary color — informative, not
  dangerous) and the "N recipes were excluded because of household allergies" banner.
- **Settings** — the "Recipe dietary facts" card (counts, backfill, progress).

## Decided trade-offs

- **Filter on AI-detected facts, but surface the exclusions.** Hard-filtering on
  unreviewed AI output contradicts the app's soft-enforcement posture; the
  compensations are the review-dialog exclusion notice and the one-click Confirm on
  the recipe page. Diets never hard-filter (mixed households cook variants) — they
  annotate and warn.
- **Pre-filtering the list is not a safety guarantee.** New generation also rejects
  unassessed dishes for attendees with canonical allergy exclusions, including
  free-text and unverified freezer choices. Missing dates stay empty in a partial
  plan. Ingredient classifications remain fallible; people must check the recipe
  and actual product declarations.
- **Demo seed recipes stay Unassessed** (the seeder won't fake AI facts with keyword
  guessing); demo members do get preset exclusion sets.
- Freezer items without assessed ingredient data cannot fill a generated meal for
  attendees with canonical allergies; Freezy does not supply those facts.

## Implementation Checklist

### November 2026 review followups

- [x] Ingredient changes and reassessment preserve revision-bound prior assessments.
  Repeated identical class sets retain their assessment times without Pending duplicates.
- [x] Explicit `POST /api/recipes/facts/backfill?includeAssessed=true` can refresh
  existing AI assessments; the default skips them and both paths protect human confirmations.
- [x] The provider facts input contains ingredients only; an untrusted recipe title
  cannot change the classification. Canonical classes and unknown-result behavior remain strict.
- [x] Independent 50-case corpus and `scripts/run-ai-facts-evals.ps1` cover all 20
  public demo recipes, exact declarations/identities and injection-like titles.
- [x] The unchanged 50-case corpus passes **150/150** over three repeats with retained
  facts contract v7, `deepseek/deepseek-v4.1-flash`, task-specific low reasoning and an
  8192-token output allowance. All 150 physical calls succeeded (average 3977ms,
  P95 14265ms). On this model, 2048 and 4096 allowances scored 135/150 and 149/150;
  a reasoning-disabled prompt experiment scored 145/150 and was reverted. The
  older v4-flash model passed 150/150 with 2048; that is a separate comparison.
  This is finite regression evidence, not complete accuracy. Stored public recipes
  separately pass **20/20 minimum assertions** after refresh. Neither result proves
  unspecified product composition, all extra classes or future completions.

`Ai__FactsEnableReasoning` defaults to false; when enabled the central execution gate
requests low reasoning for facts only and requires at least 1024 output tokens.
Generic output/concurrency defaults remain 512/1. The measured local DeepSeek profile
uses reasoning enabled, 8192 output tokens and concurrency four. Benchmark overrides:
`scripts/run-ai-facts-evals.ps1 -EnableFactsReasoning -MaxOutputTokens 8192 -Iterations 3`.

See [current classification and identity evidence](../reviews/dish-identity-fix-2026-11-14.md).

- [x] `IngredientClass` (EU-14 + meat split + extras) + `RecipeDietaryFact` +
      revisioned assessment status on Recipe + `FamilyMemberDietaryTag.ExcludedClasses` (CSV) +
      migration incl. preset seeding of existing member-tag links
- [x] `DietaryTagPresets` (Dutch/English synonyms) + unit tests
- [x] Member DTOs/sync on tag entries with per-link classes; preset endpoint;
      family-page definition editor
- [x] `LlmRecipeFactsExtractor` + NoOp + `RecipeFactsAssessmentService` (queue) +
      hooks (URL import, create/update, file import) + status/backfill/facts endpoints
- [x] Recipe form "Contains" card + detail badge/confirm/edit + settings card
- [x] Planner: `AllergyExcludedRecipeIds` pre-filter, prompt annotations, protected-
      prompt rule, `FlagConstraintConflicts` + `Finalize` coverage fix, rules-fallback
      skip, `DietWarning`/`excludedForAllergies` DTOs + review-dialog UI
- [x] `dishhive:dietaryFacts` in export/import; capability-fixture facts
- [x] Tests: presets, prompt content, exact/heuristic/diet flagging, fallback-path
      flagging regression, rules skip, facts endpoints, exchange round trip
