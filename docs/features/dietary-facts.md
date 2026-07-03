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
- **`Recipe.DietaryFactsStatus`** — tri-state, load-bearing: `Unassessed` (an empty
  fact set means "unknown", NEVER "contains nothing"), `AiDetected`, `UserConfirmed`.
  Plus `DietaryFactsAssessedAt`.
- Only assessed recipes participate in exact matching; unassessed ones behave exactly
  like before this feature (substring net only).

## AI assessment pipeline

- **`LlmRecipeFactsExtractor`** (`Services/Facts/`) — one-shot `IChatClient` call
  (same posture as `LlmRecipeExtractor`: NoOp when AI unconfigured, does not consult
  the capability service, failure → null and the recipe stays Unassessed). The prompt
  asks for factual containment only — never diet verdicts — and is deliberately biased
  to over-include on uncertain processed products: a false "contains" surfaces visibly
  (warning/exclusion the user can correct), a missed allergen does not. Derived
  ingredients are called out (boter→Milk, sojasaus→Soybeans+Gluten, vissaus→Fish, …).
- **`RecipeFactsAssessmentService`** — singleton `Channel<Guid>` queue +
  `BackgroundService` worker, deliberately sequential (a local LM Studio model must
  not get parallel calls; the settings page gets a meaningful remaining count).
  Enqueueing is fire-and-forget and never blocks the save that triggered it; with AI
  unconfigured `TryEnqueue` is a visible no-op. `UserConfirmed` facts are never
  overwritten **except** on ingredient edits, where the confirmed facts describe
  ingredients that no longer exist — a fresh AI assessment beats a stale human one.
  Recipes without ingredients are skipped (title-only classification would be exactly
  the world-knowledge guessing this feature replaces).
- **Hooks:** URL import + re-import (`RecipeImportService`), manual create, update
  with a changed ingredient list (`RecipesController`), file import for recipes
  arriving without facts (`RecipeExchangeService`).
- **Backfill** (user decision: manual, only unassessed, visible progress): the
  settings-page card shows unassessed/AI-detected/confirmed counts and an "Assess N
  unclassified recipes" button → `POST /api/recipes/facts/backfill`; while the queue
  runs the card polls `GET /api/recipes/facts/status` (2s, integrations-status
  pattern) and shows the remaining count.

## API

- `RecipeDto.dietaryFacts` = `{ contains: string[], status, assessedAt }`; create/
  update DTOs take optional `containsClasses` (null = untouched → AI queue on create,
  stored facts kept on update; a list = user-set → `UserConfirmed`).
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
- **Pre-filtering the list is not a safety guarantee.** The model can still invent
  free-text dishes and external `@[Source]` picks; the prompt rule and the warning
  net stay. For real anaphylaxis-grade allergies the app is advisory — the human
  checks the recipe.
- **Demo seed recipes stay Unassessed** (the seeder won't fake AI facts with keyword
  guessing); demo members do get preset exclusion sets.
- Freezer items remain uncheckable (Freezy has no ingredient data) — pre-existing,
  documented gap.

## Implementation Checklist

- [x] `IngredientClass` (EU-14 + meat split + extras) + `RecipeDietaryFact` +
      tri-state status on Recipe + `FamilyMemberDietaryTag.ExcludedClasses` (CSV) +
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
