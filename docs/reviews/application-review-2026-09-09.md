**Dishhive application review and improvement proposal — 9 September 2026**

Reviewed repository revision: `73aa2ab`. This is a proposal; application code and existing tests were not changed.

The application has a sound foundation for a household product. Keep the single deployable application, optional integrations, central AI execution seam, typed AI contracts, deterministic candidate verification, and explicit suggestion review. The highest-value work is to strengthen the boundaries between mutable recipes, AI results, final planning validation, and persistence. A framework rewrite or a move to microservices would add cost without addressing the defects below.

**Evidence and limits**

The review covered startup and deployment, controllers and persistence, AI execution and capability testing, planning intent/research/repair/fallback/acceptance, recipe import and background assessment/translation, frontend services/templates/styles, tests, and the planning evaluation runner.

| Check | Result |
| --- | --- |
| Backend test suite | **434 passed**, 0 failed, 0 skipped; approximately 2 seconds of test execution |
| Frontend test suite | **59 passed** across 13 files; approximately 9.6 seconds |
| Angular production build | Passed; initial bundle about 654 kB raw / 153 kB estimated transfer; no reported budget failures |
| Browser audit | 12 captures at 360×900/light and 12 at 1280×900/dark; all 24 inspected; no reported page-level horizontal overflow or browser JavaScript exceptions |
| Additional visual inspection | Three scrolled mobile captures: task metrics, AI settings, recipe ingredients |
| Week-navigation race | Reproduced using controlled response ordering in the running Angular component |
| Live AI evaluation | Completed all four active scenarios, three iterations each: **9 passed, 3 failed**, with three latency warnings. Passing cases also exposed semantic and allergy-validation gaps; see below |

Initial test runs encountered sandbox restrictions on Windows host logging and esbuild process creation. Both suites passed after rerunning with the required execution access. These environment failures are not counted as application defects.

Browser checks used the current Angular source against the existing local container API. That backend was not rebuilt or migrated for the review. API mutations and model generation were blocked in the browser audit. Consequently, these captures establish the observed UI behavior, not every production backend path. They cover populated recipes/family/statistics, empty current-week/history/shopping states, recipe create/edit/detail, and suggestion composition with AI unavailable. Generated-result states, all other dialogs, Dutch layouts, the full viewport/theme matrix, production service-worker behavior, and real concurrent PostgreSQL writes still need coverage. Full-page screenshots do not automatically reveal all content inside the app's scrolling container; the extra scrolled captures address selected areas only.

Disposable evidence is in [artifacts/application-review](../../artifacts/application-review): `review-backend.trx`, `backend-tests.log`, `ui-report.json`, screenshots, `week-race.json`, and the read-only browser probes. These artifacts are ignored by Git and should be retained separately if the review is shared.

**Live AI validation — 9 September 2026**

Dishhive now targets **`qwen/qwen3.8-27b:2`**, the LM Studio instance loaded with a **65,820-token context**. A fresh application capability test passed all eight checks in **54.7 seconds**, using JSON Schema output. The running configuration has a 20,000-token input budget, 12,000-token output allowance, temperature 0.3, thinking disabled, a 60-second completion timeout, one configured retry, and a 300-second agent timeout. SearXNG was available.

The repository runner then executed every active planning scenario three times against the existing local data. It generated suggestions without accepting them. Total measured scenario time was about 8.5 minutes.

| Scenario | Corpus assertions | Durations | Application outcomes |
| --- | --- | --- | --- |
| Distinct vegetarian source | 3/3 passed | 37.6 / 23.0 / 22.2 s | Three `success` |
| Multiple sources, desserts and chicken Friday | 3/3 passed | 96.3 / 80.1 / 57.7 s | Two `qualityFallback`, one `partialFallback` |
| Dutch distinct source request | 3/3 passed | 70.5 / 67.5 / 43.2 s | Three `partialFallback` |
| Saved recipes stay linked | 0/3 passed | 3.8 / 3.3 / 3.3 s | Three `intentFallback`; zero of three required recipe links each time |

Three runs exceeded advisory latency budgets: one above 90 seconds and two above 60 seconds. The known-limitation Dutch Friday pancakes scenario was skipped by the runner's default policy. These results describe this model/configuration and current household, recipe, freezer, and search data; they are not an isolated benchmark.

**The nine assertion passes overstate product correctness.** In the first two multi-source runs, the final Friday chicken recipe carried a Milk allergy conflict for a household attendee, despite the validator rejecting that same local recipe during corrective attempts. The third run returned unverified **Chili con Carne** for the requested chicken slot, claiming its constraint and displaying a diet warning. Its corpus assertion still passed because the slot's `requiredClasses` is empty. This does not establish that the returned dish contains chicken. The returned dessert also carried an allergy warning. Warnings are visible in the API response, so the problem is inconsistent hard-constraint enforcement and a permissive evaluation oracle, rather than missing warnings.

All three saved-recipe failures have the same observed trigger: the intent model emitted `sourceHost: "dishhive"` for “already saved in my Dishhive recipe store.” [Intent parsing](../../src/Dishhive.Api/Services/Suggestions/PlanningIntent.cs#L196) rejects that host because it is absent from the allowed external sources. The service immediately falls back to rules; all three returned items came from the freezer with null recipe IDs. This fails before local recipe selection. A typed local-only selection requirement is missing from the intent contract, and fallback does not preserve that explicit user restriction.

The earlier availability problem is resolved, but it remains useful diagnostic evidence: the app initially targeted another model, then the unsuffixed `qwen/qwen3.8-27b` instance with only 8,192 tokens of context. LM Studio rejected a capability prompt containing **44,388 tokens** before generation. The app's character estimate and nominal 20,000-token input budget did not bound the actual prompt. Targeting the loaded `:2` instance made the capability test pass. This reinforces R7/R8/R9: distinguish reachability, selected model instance, context capacity, cached readiness, and semantic task outcome.

Evidence: [capability result](../../artifacts/application-review/capability-success.json), [complete evaluation report](../../artifacts/application-review/live-planning-3x.json), [condensed results and final suggestions](../../artifacts/application-review/live-planning-summary.json), [operation metrics](../../artifacts/application-review/live-planning-metrics.json), and [validation log excerpts](../../artifacts/application-review/live-planning-validation.log). These are ignored local artifacts.

**Priority and interpretation**

P1 means address before expanding reliance on the affected behavior. P2 means important reliability or maintenance work. P3 means a usability improvement or planned capability. Source-confirmed findings describe an identifiable code path; they are distinguished from runtime reproductions and risks requiring concurrency or live-model testing.

| ID | Priority | Finding | Evidence |
| --- | --- | --- | --- |
| R1 | P1 | Ingredient edits leave obsolete dietary facts marked current | Code and an existing test explicitly preserve this behavior |
| R2 | P1 | Background AI results can overwrite a newer recipe or human assessment | Source-confirmed race; worker concurrency not reproduced against PostgreSQL |
| R3 | P1 | Final suggestions can violate requirements after validation; some requirements never reach structured intent | Source-confirmed paths plus live allergy/local-only violations and a weak chicken-slot assertion |
| R4 | P1 | Accepting suggestions drops explicitly selected guests | Source-confirmed mismatch between generation and acceptance |
| R5 | P1 | Late frontend responses overwrite the currently selected week | Browser reproduction; analogous recipe-filter path found |
| R6 | P2 | Acceptance does not reserve freezer stock atomically or preserve the reviewed external-recipe version | Source-confirmed gaps; concurrent writes not stress-tested |
| R7 | P2 | AI gate guarantees depend on callers; transient capability failures persist indefinitely | Source-confirmed execution and cache behavior |
| R8 | P2 | AI input/deadline limits and workload scheduling are incomplete | Source-confirmed budget and concurrency paths |
| R9 | P2 | Telemetry mixes meanings and cannot reliably compare whole operations/configurations | Code and retained metrics displayed in the UI |
| R10 | P2 | Tests cover many functions but miss critical persistence, workflow, and operational behavior | Test inventory and execution |
| R11 | P2 | UI colors are consistent; typography, compact controls, and shapes drift | Source inspection and screenshots |
| R12 | P2 | API/domain limits and manually duplicated frontend contracts disagree | Concrete 300-character recipe / 200-character suggestion mismatch |
| R13 | P1 when reachable by untrusted clients | Default network exposure includes unauthenticated write and package-update endpoints | Program, Compose, and scraper source |
| R14 | P2 | Runtime scraper updates are destructive before success is established | Source-confirmed update sequence |

**R1 — Invalidate dietary assessments when their source changes**

[RecipesController.cs:441–456](../../src/Dishhive.Api/Controllers/RecipesController.cs#L441) saves edited ingredients and merely queues reassessment. It does not clear or invalidate the previous status. If AI is disabled, unavailable, cancelled, or unable to classify the recipe, old facts can remain indefinitely. The same issue exists on [reimport](../../src/Dishhive.Api/Services/Import/RecipeImportService.cs#L128).

This is an existing product rule, not simply an untested accident: [RecipeFactsIntegrationTests.cs](../../src/Dishhive.Api.Tests/Integration/RecipeFactsIntegrationTests.cs#L118) contains `UpdateRecipe_ChangedIngredients_ResetsNothingWhenAiUnavailable`, which expects the stale result to remain `UserConfirmed`.

Example: a recipe confirmed with no tracked classes is edited to add milk. Until a new assessment succeeds, it can still appear to have a current confirmed empty assessment. Keeping the old data for reference is useful; representing it as confirmation of the new ingredients is misleading.

**Proposal:** persist an ingredient revision/hash and the revision assessed. Invalidate the current assessment in the same save as the ingredient edit, regardless of AI availability. Preserve the old assessment as historical evidence and expose `stale`, `pending`, `failed`, and current assessment states clearly. Planning should treat stale facts as unknown. Preserve the existing ability to confirm new facts explicitly in the same edit. Replace the test above with assertions for invalidation, retained history, and explicit reconfirmation. This requires an intentional change to the documented behavior.

**R2 — Make background work durable and conditional on the source revision**

[RecipeFactsAssessmentService.cs:110](../../src/Dishhive.Api/Services/Facts/RecipeFactsAssessmentService.cs#L110) loads and tracks a recipe, awaits potentially slow model calls, then saves. There is no revision comparison or concurrency token before applying facts or translated text. Meanwhile, [TryEnqueue at line 70](../../src/Dishhive.Api/Services/Facts/RecipeFactsAssessmentService.cs#L70) rejects a second enqueue for an already pending recipe.

A realistic sequence is: assessment reads version A; a person edits the recipe to version B; the new enqueue is discarded; the worker finishes and applies results for A. A human confirmation made during assessment can likewise be overwritten, depending on the changed rows and result. Replacement/deletion of ingredient rows can instead cause a save failure. Neither outcome reliably schedules the newest version.

The unbounded in-memory channel also loses pending work on restart. Facts and translation share one work item, database scope, and final save; a translation exception can prevent already-computed facts from being persisted. The queue is globally sequential only within this worker, not across every use of the shared model.

**Proposal:** use a small database-backed job table, with recipe revision, task, target language, state, attempt count, lease, and bounded retry time. Take an immutable input snapshot, release the database scope during model execution, and conditionally apply the result only if the source revision and confirmation state still match. Coalesce toward the newest revision rather than dropping an update. Separate facts and translation jobs while preserving a common operation ID. Recover expired leases on startup. Test edit-during-assessment, confirmation-during-assessment, coalescing, restart, and cancellation.

**R3 — Validate the final domain plan and represent unmet requirements**

The planning code has useful validation, but its placement and coverage leave gaps:

- [LlmMealSuggestionService.cs:205](../../src/Dishhive.Api/Services/Suggestions/LlmMealSuggestionService.cs#L205) validates the model payload. Later, post-processing resolves aliases, removes suggestions, deduplicates dishes, limits items, and links freezer meals; rules backfill can add suggestions. There is no equivalent full validation after all those transformations. A payload can pass and the returned plan can then lose a required dish or count.
- At [lines 275–299](../../src/Dishhive.Api/Services/Suggestions/LlmMealSuggestionService.cs#L275), unresolved quality issues after repair still lead to returned suggestions. `qualityFallback` is a telemetry outcome; it is not a structured unmet-requirements result in the product response. A plan containing LLM rows can avoid the dialog's all-fallback banner even when requirements remain unmet.
- [BuildPlanningIntentPrompt](../../src/Dishhive.Api/Services/Suggestions/LlmMealSuggestionService.cs#L348) includes global instructions, selected dates, and source names/hosts, but omits dated `WeekPlan.VagueInstruction` text and collection constraints. With only a day-level idea and no source/global instruction, the intent call can be skipped entirely. The final planning model sees more instructions than the deterministic validator does.
- [PlanningIntentValidator](../../src/Dishhive.Api/Services/Suggestions/PlanningIntentValidator.cs#L14) verifies counts, classes, source provenance, and selected identifiers, but does not validate `DishRequest`. Collection membership is only logged in [MealSuggestionPostProcessor](../../src/Dishhive.Api/Services/Suggestions/MealSuggestionPostProcessor.cs#L163). The typed intent has no cooking-time ceiling or freezer-meal cap. A request for a specific dish or a quick meal therefore has weaker enforcement than its natural-language presentation suggests.

The live evaluations above reproduce this boundary failure: two `qualityFallback` plans retain a local recipe the validator rejected for an allergy conflict, and all three local-only requests return unlinked freezer meals after intent rejection. The chicken-slot pass with unverified Chili con Carne also shows that a claimed constraint and warning do not prove the requested food class.

**Proposal:** normalize all explicit instructions into one domain request with dates, meal/course, attendee set, recipe/collection/source identities, hard constraints, and preferences. Add an explicit allowed-origin policy for local recipes, external candidates, and freezer items; do not encode the local store as an external host. Give invalid intent a bounded corrective attempt with structured validation errors, and preserve explicit restrictions when declaring the request incomplete. Resolve a specific named local dish into candidate IDs. Keep model language interpretation as a separate uncertain step. Validate resolved `MealSuggestion` objects after repair, filtering, and fallback using one shared invariant checker. Return explicit status such as complete/partial/unable, plus unmet requirements and per-item provenance. Backfill must satisfy hard constraints or leave a visible gap. Do not label unresolved requirements as a successful plan merely because usable JSON was returned.

Introduce a bounded deterministic candidate selector before considering a general optimization solver. Preserve the ability to review partial plans. Add cases where alias resolution creates duplicates, post-processing drops a required candidate, a day idea contains the only source request, a collection is mandatory, and fewer valid candidates exist than requested.

**R4 — Preserve selected guests through acceptance**

[MealSuggestionRequestBuilder.cs:50](../../src/Dishhive.Api/Services/Suggestions/MealSuggestionRequestBuilder.cs#L50) includes active guests when their IDs are explicitly requested. [MealSuggestionAcceptanceService.cs:25](../../src/Dishhive.Api/Services/Suggestions/MealSuggestionAcceptanceService.cs#L25) always loads only active non-guests and filters the suggestion's attendee list against them. A household-plus-guest suggestion silently loses the guest; a guest-only suggestion can fail with no active attendees. Shopping quantities then reflect the reduced attendee count.

**Proposal:** use active non-guests only for default attendance. Validate explicit attendance against all active members, including guests. Share that policy between generation, quick planning, manual planning, and acceptance. Add endpoint tests for household-plus-guest, guest-only, inactive-member, and deliberately empty/default selections.

**R5 — Make frontend reads follow the latest selection**

[WeekPlannerPage.loadWeek](../../src/dishhive-web/src/app/pages/week-planner/week-planner.page.ts#L146) creates a new subscription for each navigation and unconditionally applies every response. The browser probe loaded September 14 after September 7, delivered the September 14 response first, then delivered September 7. The selected week remained September 14 while `meals` was replaced with September 7 data. This can present an apparently empty week because grouping uses the currently selected dates. Evidence: [week-race.json](../../artifacts/application-review/week-race.json).

[RecipesPage.loadRecipes](../../src/dishhive-web/src/app/pages/recipes/recipes.page.ts#L108) has the analogous problem: debouncing the search input does not cancel an earlier request after it has started. The shopping-list search should be reviewed for the same ordering behavior.

**Proposal:** drive queries from selection streams and use `switchMap` for reads, with lifecycle cancellation and a loading state tied to the active request. Alternatively, guard responses with a request sequence ID. Add controlled out-of-order response tests. Keep mutation handling separate so saving a meal is not cancelled just because a new UI selection occurs.

**R6 — Strengthen suggestion acceptance as a persistence boundary**

Acceptance already has valuable per-item results and a unique suggestion key, so sequential retries avoid duplicate meals. However, [stock is read before the transaction](../../src/Dishhive.Api/Services/Suggestions/MealSuggestionAcceptanceService.cs#L61). Two different batches can both observe the final freezer unit and both create a reservation. The suggestion-key index does not prevent this because the keys differ. This is a source-confirmed race, not a measured PostgreSQL stress-test result.

External suggestions are [refetched/imported on acceptance](../../src/Dishhive.Api/Services/Suggestions/MealSuggestionAcceptanceService.cs#L83). The persisted batch ID is effectively client-carried identity; there is no durable reviewed candidate version or final revalidation of the imported ingredients against the reviewed constraints. Imports can also persist before planning fails, which should have explicit product semantics.

**Proposal:** make local freezer reservations atomic per item using a relational lock or a transactional reservation ledger. Treat external Freezy inventory as a changing snapshot and expose reconciliation; a local lock cannot control stock changes made in Freezy itself. Persist lightweight suggestion batches with candidate/source revision and expiry. Revalidate changed facts and attendees before saving, and return `needsReview` when the imported recipe materially differs. Preserve per-item retry behavior and make any intentionally retained imported recipe visible after a planning failure. Use PostgreSQL concurrency tests for these guarantees.

**R7 — Turn the AI gate's conventions into enforceable contracts**

All production completion calls found go through `IAiExecutionGate`; this is worth preserving. But [ExecuteAsync](../../src/Dishhive.Api/Services/Ai/AiExecutionGate.cs#L105) does not itself enforce the capability decision exposed by `EnsureAvailableAsync`. `ReportOutcomeAsync` accepts any string and can be omitted or called repeatedly. A model result is persisted as `completed` before semantic validation, and a crash or parser exception can leave it there.

Separately, [AiModelCapabilityService](../../src/Dishhive.Api/Services/Suggestions/AiModelCapabilityService.cs#L90) memoizes the result and reuses persisted failed results without a time-based retry policy. A transient startup outage can keep planning on fallback until manual retest or a fingerprint change, even across restarts. Capability-test work uses `CancellationToken.None`, so host shutdown only cancels the wait, not the shared test itself. The verdict's viability primarily means that a response mode worked; failed semantic checks can still yield a warning rather than block planning.

**Proposal:** enforce task eligibility inside execution, with a deliberate internal probe exemption. Use typed terminal outcomes and a validated-execution helper so transport and semantic completion cannot drift apart. Separate transient availability, response-format compatibility, and task-quality verdicts. Retry transient probe failures after a cooldown, maintain a reasonable expiry for capability evidence, and tie detached work to application lifetime. Label what the capability test actually proves. Retain manual retesting for model changes behind an unchanged endpoint/model name.

**R8 — Apply budgets to the whole operation and schedule model work**

[MealSuggestionPromptBuilder](../../src/Dishhive.Api/Services/Suggestions/MealSuggestionPromptBuilder.cs#L248) budgets the recipe/history tail after adding other context. The full call can additionally contain the system prompt, unbounded collection/favorite context, normalized intent, candidate ingredients/instructions, and repair messages. The semantic-repair path [appends the entire previous answer](../../src/Dishhive.Api/Services/Suggestions/LlmMealSuggestionService.cs#L223). Facts extraction sends the complete ingredient list without its own input budget. Translation chunks preserve content, which is good, but their character estimate is not a model-context guarantee.

The capability wait occurs before the planner's timeout is created. Non-planning retries each get a new per-call deadline. Source planning adds a retry outside the task profile. None of these is necessarily wrong individually, but the advertised timeout/retry configuration does not describe a single consistent end-to-end budget. There is also no shared model admission limit: background facts, translations, capability probes, imports, and interactive suggestions can compete concurrently.

**Proposal:** introduce a logical-operation context with deadline, cancellation, correlation ID, total attempt limit, and input/output budget. Include all messages and expected output headroom in the budget; bound repair quotations and candidate details. Validate configuration at startup. Prefer trimming ranked optional context or chunking lossless transformations over silent truncation of essential source material. Add a bounded per-provider queue with interactive planning ahead of bulk enrichment, and record queue wait separately from generation time. Make retries and rate-limit backoff explicit and observable.

**R9 — Make telemetry describe product outcomes and comparable cohorts**

Per-task retention is a good choice: [AiTaskMetricsStore](../../src/Dishhive.Api/Services/Ai/AiTaskMetricsStore.cs#L17) retains 250 rows per task, preventing facts volume from erasing every low-volume task. However, [the statistics endpoint](../../src/Dishhive.Api/Controllers/IntegrationsController.cs#L245) groups by task only and combines configurations and dates. All outcomes other than success/noResult are counted as failures, including caller cancellation and unfinished `completed` rows. The visible call list is truncated before the frontend task filter runs, so a task summary can have data while its detail filter has no matching recent calls.

The captured retained data illustrates why the aggregate needs context: the page showed 93% overall call success and 69% week-planning call success. Those are historical retained samples, not measurements of the current model. The 100% facts figure means accepted structured output; it does not measure classification accuracy. Successful deterministic repair is also a different result from a correct first model answer.

Facts/translation/import calls do not consistently share the logical product operation ID, and `AiPlanningRun` lacks a full execution/prompt/configuration snapshot. Raw instructions and normalized intent are retained and returned, so diagnostic privacy is broader than the generic task-row design suggests.

**Proposal:** filter server-side by time, task, model, fingerprint, outcome, and operation. Distinguish transport success, contract validity, semantic validity, final operation completeness, cancellation, and abandoned work. Record accepted/rejected suggestions and manual corrections as product-quality signals, without conflating them with model-call success. Carry one operation ID through import, facts, translation, and planning research. Store prompt/version hashes and input revision identifiers. Make diagnostic text retention explicit and optional, with bounded sanitized errors and a purge path. Move retention cleanup off the foreground call path if volume warrants it.

**R10 — Spend test effort on the missing guarantees**

The suite is substantial and much of it is useful. Keep the offline scraper fixtures, strict generated-schema and unknown-enum tests, planning validation/repair cases, URL/redirect protections, cancellation behavior in the suggestion dialog, and acceptance idempotency tests. They protect real boundaries.

The main weakness is the kind of evidence available. [TestWebApplicationFactory](../../src/Dishhive.Api.Tests/TestWebApplicationFactory.cs#L31) uses EF InMemory. Consequently, successful endpoint tests do not establish PostgreSQL query translation, unique-key races, transaction rollback, migration correctness, or deletion behavior. In particular, acceptance skips its relational transaction branch under InMemory. Microsoft's guidance also cautions against treating that provider as a substitute for the production database. [EF Core testing strategy](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy).

The frontend suite has no page tests for week navigation, recipe filtering, family editing, shopping-list workflows, or the main settings page. Helper tests around quick planning do not exercise the full Material dialog. The central execution gate has only a small set of direct settings/schema tests; timeout, cancellation, transport error, and terminal-outcome persistence deserve focused cases. No scraper-sidecar test suite or single local verification command was found.

Low-value candidates are limited, rather than pervasive: the app's constant-title assertion and creation-only smoke assertion contribute little compared with its actual navigation test. Prompt-fragment tests protect composition, but should not be presented as evidence that a model follows those instructions. The corpus-shape test verifies that JSON cases are runnable; it does not execute them. The R1 test is especially important because it locks in a questionable product rule despite passing.

The live run demonstrates both the value and limits of the corpus: it consistently catches lost local recipe links, but all three chicken scenarios pass despite two allergy conflicts and one unverified dish. Strengthen the [scenario assertions](../../src/Dishhive.Api.Tests/Fixtures/AiWeekPlanning/scenarios.json#L58) to require verified Poultry for the chicken slot, reject known allergy conflicts for assigned attendees, and assert final allowed origins and unmet-requirement status. Record application outcomes alongside assertion results so `qualityFallback` cannot appear as an unqualified success. Add a captured invalid-intent fixture for `sourceHost: "dishhive"` and separate syntax errors from semantic intent rejection in telemetry.

| Test layer | Proposed additions | What they prove |
| --- | --- | --- |
| Domain/unit | Final plan invariants, explicit dish/collection/time requirements, repair/backfill cases, unknown and stale fact states | Correctness independent of a particular model |
| Worker | Revision races, human confirmation races, latest-revision coalescing, restart recovery | AI results cannot silently overwrite newer work |
| PostgreSQL integration | Guest acceptance, concurrent freezer reservations, concurrent idempotency, rollback, migrations, source URL uniqueness | Actual persistence behavior |
| AI gate/HTTP adapter | Per-task deadlines, caller cancellation, provider timeout/error/429, terminal outcomes, schema downgrade, input limits | Operational guarantees around the model |
| Angular components | Out-of-order week/filter reads, guest display, failed saves, partial acceptance and retry, preference initialization | User workflows and state transitions |
| Browser | Main planning journey, recipe import/edit, keyboard dialog behavior, mobile long labels, theme changes, production offline/cache behavior | Rendered and cross-component behavior |
| Live evaluations | Expanded task corpora, pinned configuration, repeated runs, explicit known limitations | Measured model quality and variability |

Add a local verification command for backend tests, PostgreSQL tests, frontend tests/build, and generated-contract drift. The existing mobile audit records geometry failures but does not turn them into a failing exit code, and its review probe can invoke live generation. Give it a fixture mode, semantic selectors, failure thresholds, and screenshot baselines. Keep stochastic model evaluations as an explicit separate job, with at least three iterations for comparisons. Expand beyond the four active planning cases to facts, translation, extraction, instruction injection in recipe text, conflicting requirements, unavailable search, and incomplete candidates.

**R11 — Normalize typography and interactions while preserving the existing theme**

The requested CSS review did **not** find component-local hex/RGB/HSL color literals. The app already uses `--mat-sys-*` colors, shared content-width tokens, Material theme APIs, responsive wrapping, and bounded table scrolling. Most local layout CSS has a legitimate purpose. The remaining work is mainly typography, shape, repeated badges, and controls whose content exceeds their intended layout.

| Surface | Observed issue | Proposed change |
| --- | --- | --- |
| [AI task statistics](../../src/dishhive-web/src/app/pages/ai-planning-stats/ai-planning-stats.page.scss#L24) | Four-line metric content is placed in stroked buttons with `height:auto`; text touches the rounded outline in mobile and desktop captures | Use a proper card for the metrics and a separate compact Material filter control, or a deliberately themed interactive card; verify keyboard/pressed semantics |
| [AI prompt settings](../../src/dishhive-web/src/app/components/ai-prompt-settings/ai-prompt-settings.scss#L1) | 10/12/13/14px text, custom line heights, badge spacing and radii | Use Material body/label/title roles; retain monospace where reading a prompt benefits from it |
| [Suggestion review](../../src/dishhive-web/src/app/components/suggestion-review-dialog/suggestion-review-dialog.scss#L97) | Small custom uppercase labels, badges, metadata, warning sizes and row hover styling | Use shared semantic status/warning presentation and Material typography; keep unique week layout local |
| [Quick-plan dates](../../src/dishhive-web/src/app/components/quick-plan-dialog/quick-plan-dialog.scss#L28) | Custom button-like rows recreate hover/focus and use 5px gaps/11px labels | Prefer appropriate Material list/selection behavior; retain the domain-specific date/meal layout |
| [Recipe create form](../../src/dishhive-web/src/app/pages/recipe-form/recipe-form.page.html#L40) | Empty image state displays “Choose image” twice; desktop placeholder consumes much of the first screen | Consolidate to one primary upload action and use a deliberate empty-state image height |
| [Recipe collection controls](../../src/dishhive-web/src/app/pages/recipes/recipes.page.scss#L60) | Custom compact chips/creation control form another small interaction system | Evaluate Material chips for filter semantics; use a named collection component if behavior remains custom |
| [Statistics table](../../src/dishhive-web/src/app/pages/statistics/statistics.page.scss) | Long mobile dish names wrap densely against row boundaries | Keep the horizontal scroller and hint; add appropriate cell breathing room and test long titles |

Use Material typography classes or font tokens for ordinary text, and public component override mixins for Material shape/appearance. The current official theming guide supports both system variables and component overrides. [Angular Material theming](https://github.com/angular/components/blob/main/guides/theming.md).

Create a shared badge/warning component only for matching semantics across multiple consumers. Do not turn every pixel into an application variable. Keep image aspect ratios, icons, one-pixel borders, illustration geometry, deliberate content widths, and justified breakpoints local. The family chip `::ng-deep` overflow rules and the global slide-toggle internal selector are already documented exceptions; keep their upgrade risk visible and test them before attempting replacement. Global typography/density or control-hierarchy changes need a separate visual proposal with before/after captures.

Evidence: [mobile metric controls](../../artifacts/application-review/mobile-task-cards.png), [new recipe](../../artifacts/application-review/ui-360-light/recipes-new.png), [desktop metrics](../../artifacts/application-review/ui-1280-dark/settings-ai-stats.png), and [mobile statistics](../../artifacts/application-review/ui-360-light/statistics.png).

**R12 — Align API contracts and extract cohesive application operations**

A concrete mismatch already exists: [recipe titles allow 300 characters](../../src/Dishhive.Api/Models/DTOs/RecipeDtos.cs#L116), while [accepted suggestion dish names allow 200](../../src/Dishhive.Api/Models/DTOs/MealSuggestionDtos.cs#L120) and the planned-meal model also limits the name to 200. A valid stored recipe with a 250-character title can be suggested and then cause acceptance validation to reject the submitted batch. This was identified from the contracts, not exercised against the user's data.

Frontend API integration is only partly generated: [nswag.json](../../src/dishhive-web/nswag.json#L26) disables client classes, while services and several model interfaces remain handwritten. The suggestion interface already omits some diagnostic fields returned by the server. Explicit UI view models can be useful, but accidental duplicate wire contracts create drift.

**Proposal:** centralize relevant domain limits and define title/name handling consistently. Generate wire DTOs and clients from a build-produced OpenAPI document with stable unique operation IDs; avoid requiring a manually running server just to verify drift. Keep UI models only where they intentionally transform the wire shape. Standardize error responses so failed validation, conflicts, and transient failures can be handled predictably.

The 952-line recipes controller owns image handling, recipe edits, tags, dietary facts, imports, and exchange endpoints. Extract cohesive application services around these mutations, including invalidation and concurrency checks, while leaving controllers as HTTP adapters. Keep EF Core as the persistence boundary; an additional generic repository layer is not needed merely to reduce controller length. Add pagination and server-side filtering before growing the recipe library substantially; the current list path materializes all matching recipes, and decoration loading adds per-member favorite calls.

**R13 — Make the deployment trust boundary explicit**

[Program.cs](../../src/Dishhive.Api/Program.cs) intentionally exposes unauthenticated REST and MCP endpoints. [Compose](../../docker-compose.yml#L92) publishes the app, database, scraper and search ports without a loopback binding. The scraper exposes [POST /update](../../src/dishhive-scraper/app.py#L93), and the app provides its own update and AI retest controls. Anyone able to reach those endpoints has more capability than simply viewing recipes. CORS does not establish authorization for direct clients.

This is conditional deployment severity: the design can be acceptable within a deliberately trusted environment, but “local/self-hosted” alone does not enforce that environment. The inspected containers were published on all host interfaces; firewall reachability and Internet exposure were not tested.

**Proposal:** bind development-only ports to loopback and keep database/scraper/search services internal in the normal deployment. Document separate trusted-local and remote-access configurations. For remote/shared access, add authentication and authorization to writes, diagnostics, AI operations and administrative actions; give MCP explicit read-scoped credentials. Add bounded request sizes and rate/concurrency limits to expensive operations. Replace committed development database credentials in a real deployment profile. Preserve the existing SSRF protections: the safe fetcher validates redirects and checks public addresses again at connection time, which is a stronger design than a single pre-fetch DNS check.

**R14 — Make scraper upgrades recoverable**

[app.py:100](../../src/dishhive-scraper/app.py#L100) deletes the active override directory before pip installation succeeds, then restarts the process on success. A failed or concurrent update can leave the override directory missing or partially populated, with no atomic switch or rollback. The version input is not validated by the update endpoint, although version parsing exists elsewhere. This review did not execute an update.

**Proposal:** prefer building and replacing a pinned scraper image. If in-app updates remain a requirement, validate the version, serialize updates, install into a separate versioned directory, smoke-test imports/extraction, then atomically activate it while retaining the previous version. Report update state durably and test failure, timeout, concurrent update, restart, and rollback. Add a small Python suite for extraction normalization and this lifecycle.

**Additional improvements and feature gaps**

These are proposals rather than claims that all current behavior is broken. They extend capabilities already described in [possible-features.md](../../possible-features.md) and follow the defects above.

| Priority | Capability | Benefit and scope |
| --- | --- | --- |
| P2 | Per-day attendance, including guests | One attendance policy from composition through shopping quantities; build on R4 rather than adding another independent selector |
| P2 | Explicit plan requirements and partial-plan explanation | Time ceiling, freezer-meal cap, required collections, specific dishes, and clear reasons when requirements cannot be met |
| P2 | Reviewable AI enrichment | Per-recipe pending/stale/failed state, retry, original-versus-proposed text, evidence behind ingredient classes, and source/model revision; count preservation alone does not establish translation or classification correctness |
| P2 | Persisted shopping check-off and manual items | Multi-device shopping becomes a workflow instead of a computed text export; define stable item identity before preserving checked state across regeneration |
| P2 | Ingredient canonicalization and unit normalization | Merge equivalent ingredient names/units without losing original source text or conflating dietary variants; the current aggregator keys primarily by lowercased name plus unit |
| P3 | Move/swap meals with undo | Easier adjustment after suggestions; preserve attendance, recipe links and freezer reservations |
| P3 | Cooking mode | Step focus, larger controls, timers, wake lock, serving scaling, original text access |
| P3 | Preferences for model/task routing and cost limits | Allow cheap classifiers and stronger planners after per-task quality/latency measurement exists; expose operation progress and cancellation first |
| P2 for operational readiness | Full backup/restore and migration rehearsal | Recipe export is useful interchange, but a household recovery path also needs plans, members, ratings, settings, images, and a verified restore procedure |

PWA reliability deserves a dedicated production-browser pass. [ngsw-config.json](../../src/dishhive-web/ngsw-config.json) caches broad API groups for seven days and falls back to cached reads after four seconds. [PwaService](../../src/dishhive-web/src/app/services/pwa.service.ts#L64) reports browser connectivity, which cannot establish whether a particular response came from cache or whether the API is reachable. Show a last-updated/stale indication where it affects planning, verify mutation/cache invalidation and updated recipe images, and distinguish “device online” from “data current.” Angular documents the freshness timeout as the wait before using cached data. [Service-worker configuration](https://angular.dev/ecosystem/service-workers/config). This is a test/design gap; stale-write behavior was not reproduced in this review.

Also centralize household-local date handling. Planning eligibility, history, freezer reservations and feedback checks use server `DateTime.Today`, while the browser uses its own local date. Define a household time zone and inject a clock for tests around midnight and daylight-saving changes. Complete localization of hardcoded snackbars, enum labels and units; the English UI currently still shows source category/unit strings such as `Hoofdgerecht` and `el`. Separate source content from application-owned vocabulary. Update the project guidelines, which still describe Green/Yellow theming and an older AI-only planning seam despite the implemented Amber/Cyan theme and shared multi-task gate.

**Proposed implementation sequence**

Each row should become a small set of reviewable PRs with the stated outcome. Effort is relative: S is localized, M crosses a few layers, L needs a domain/schema migration and workflow work. No calendar estimate is implied.

| Order | Work package | Effort | Completion criteria |
| --- | --- | --- | --- |
| 1 | Facts invalidation and revision checks (R1/R2) | L | Ingredient edits cannot retain current confirmation; obsolete AI results cannot overwrite newer work; race tests pass |
| 2 | Guest acceptance, title limits, latest-read handling (R4/R5/R12) | S–M each | Guests and valid recipe titles survive acceptance; out-of-order reads cannot replace the active selection |
| 3 | Final plan invariants and unmet-requirements response (R3) | L | Every returned final plan is validated; partial results explicitly identify unmet hard requirements; day-level instructions are normalized |
| 4 | PostgreSQL and browser regression gates (R10), started alongside 1–3 | M | Critical transaction/race paths and main user journeys run locally; generated API drift is detected |
| 5 | Durable enrichment jobs, AI operation budgets and capability recovery (R2/R7/R8) | L | Restart recovery, bounded concurrency, cancellation, complete terminal metrics, and transient recovery are demonstrated |
| 6 | Atomic reservations and reviewed candidate versions (R6) | M–L | Competing accepts cannot reserve the same final local unit; changed imported facts require review |
| 7 | Telemetry and evaluation expansion (R9/R10) | M | Task/configuration cohorts are comparable, logical operations are correlated, and repeated evals cover the new guarantees |
| 8 | UI normalization and contract/service cleanup (R11/R12) | M | Metric controls and typography are coherent at mobile/desktop sizes; source exceptions are documented and visually checked |
| Parallel deployment track | Network boundary and recoverable scraper upgrades (R13/R14) | M | Published services match the intended trust model; update failure leaves the previous scraper usable |
| After correctness work | Attendance UX, shopping workflow, meal movement, cooking mode | Per feature | Each feature has a domain contract, focused regression coverage, and mobile/desktop acceptance evidence |

Begin with R1–R5 and the PostgreSQL test foundation. Those changes address incorrect or misleading user-visible results and give later AI and UI improvements a reliable base.
