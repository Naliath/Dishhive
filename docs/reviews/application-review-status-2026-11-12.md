**Application review: current completion status — updated 14 November 2026**

The numbered local UI/operations, contracts, persistence, execution and telemetry work is complete. Requested-food identity passes **69/69**; facts pass **150/150** on the approved v4.1-flash model. The complete planning corpus scored **17/27** on strict completion before the latest scope decision. The clarified Friday behavior now passes **3/3**: only Belgian/Dutch pancakes count, and Friday stays empty with an explanation when none qualify. Six English/mixed-source repeats remain incomplete; one Dutch source repeat followed the accepted outage policy. Finding enough eligible source recipes remains the partial item. Deferred product features remain outside this queue.

This checklist reconciles the [original review](application-review-2026-09-09.md), [crosscheck](implementation-crosscheck-2026-09-13.md), and [repair report](implementation-crosscheck-fixes-2026-09-13.md) against the current working tree. Scope includes the substantial existing uncommitted and untracked implementation at checkout `5cbf7a8`; commit history alone does not contain these repairs. The user authorized clearing the local development database and redeploying. The current source runs at **http://localhost:5100**. Verification is local-only; no hosted CI is required.

**Latest local evidence (14 November)**

The complete command in `artifacts/final-item-2026-11-13/local-final-scoped-policy/`
passed **683 backend tests, zero skipped; 84 frontend tests; 28 fixture browser
checks plus one real API/Chrome workflow; one production PWA lifecycle; and nine
Linux scraper tests**. Generated contracts, localization, fixture typing and the
production build and offline partial-policy oracle checks also passed. EN/NL dish-matching task labels and filters were
inspected at 360px and 1280px using actual deployed metrics. Normal Freezy integration
and background jobs are restored; durable diagnostic text retention is disabled.

A complete **25-table, 24-recipe, four-member, 14-meal** backup/restore matched all
row/image digests and booted the final candidate image. The database retains all
four human-confirmed recipes with byte-for-byte identical recipe rows and fact sets.
Stored public recipes separately pass 20/20 minimum class assertions; no new
reassessment overwrote that dataset during this followup.

**Latest live outcome (14 November)**

- **Full planning corpus: 17/27** on strict completion, all nine scenarios repeated
  three times on one image/configuration. Six English/mixed-source repeats lack the
  requested source coverage. One Dutch repeat had no candidates after a source
  sitemap timeout; partial output follows the user's accepted outage policy.
- **Clarified Friday: 3/3 policy checks pass**, with four assessed saved meals and
  an explained empty Friday. The original prompt is preserved and the user's new
  clarification is supplied separately. Strict source/slot completion remains
  **0/3**, separate from approved partial behavior. Wrong styles,
  unexplained gaps and allergy violations still fail the oracle.
- **Dish identity: 69/69.** The bounded `recipe.dish_match` task, content-bound
  approvals, source-scope validation and acceptance guards are implemented.
  Replaying the old apparent Friday successes now gives 0/3: they selected unrelated
  food. Earlier source/date-only scores are not pancake completion evidence.
- **Facts: 150/150** on `deepseek/deepseek-v4.1-flash` through OpenRouter, explicitly
  approved by the user. The unchanged 50-case corpus and retained v7 contract use
  facts-only low reasoning, an 8192-token output allowance and local concurrency
  four. All 150 physical calls succeeded; average 3977ms, P95 14265ms. Console tests
  use prompted JSON; deployed calls also exercise verified strict schema. This is
  finite regression evidence, not an allergy-safety guarantee.
- **Execution audit:** 319 provider calls across 27 completed
  operations have contiguous ordinals, complete input metadata and terminal outcomes.
  Maximum 31 attempts and 23
  resolved pages remain within the shared 32-attempt/28-page limits. Per-request
  latency and advisory overruns remain visible in the detailed report.

[Final implementation, exact outcomes and remaining gaps](dish-identity-fix-2026-11-14.md).

**Status of every original finding**

“Implemented” means the reported defect has a source-level repair and regression evidence; the table states the current verification. It does not imply remote/production readiness. “Partial” means a specific part of the original proposal remains open; the completed parts are stated separately. Missing coverage is distinguished from a reproduced defect.

| ID | Assessment | Completed and available for review | Still outstanding / evidence needed to close |
| --- | --- | --- | --- |
| **R1 — stale dietary facts** | **Implemented** | Ingredient changes invalidate confirmation even with AI unavailable; revisions, assessed revision, historical facts, and explicit reconfirmation are stored. URL/file reimports invalidate replaced source data. [State transitions](../../src/Dishhive.Api/Services/Facts/RecipeFactsStateService.cs), [mutation path](../../src/Dishhive.Api/Controllers/RecipesController.cs#L472), [changed-ingredients regression](../../src/Dishhive.Api.Tests/Integration/RecipeFactsIntegrationTests.cs#L116). | Review the visible stale/pending/failed labels and explicit reconfirmation behavior. The richer enrichment review feature below is separate from this fixed defect. |
| **R2 — durable, revision-safe enrichment** | **Implemented; fresh PostgreSQL regressions passed** | Facts/translation jobs are durable and staged with recipe saves; pending work coalesces. Every poll recovers expired leases. Claims/completions compare ownership and revisions; recipe and confirmation versions are concurrency tokens. Translation guards the parent even when only child rows change. [Worker](../../src/Dishhive.Api/Services/Facts/RecipeFactsAssessmentService.cs), [mapping](../../src/Dishhive.Api/Data/DishhiveDbContext.cs#L143), [relational regressions](../../src/Dishhive.Api.Tests/Crosscheck/PersistenceCrossTests.cs#L255). | The fresh complete suite covers commit races, competing claims, lease recovery and the child-only translation race. No remaining reproduced R2 defect was found in this audit. |
| **R3 - final plan requirements** | **Constraint, identity and partial-policy repairs implemented; source quality partial** | Intent v11 preserves collections, counts, dates, origins, facts, attendees and requested food. Bounded matching, content approvals, independent style assertions and acceptance rechecks reject unrelated or changed candidates. The clarified Belgian/Dutch-only Friday policy passes 3/3; gaps stay empty with dated explanations. [Current evidence](dish-identity-fix-2026-11-14.md). | Full strict planning is 17/27. Six English/mixed-source repeats remain incomplete; one Dutch source-sitemap outage follows accepted partial behavior. Strict Friday meal completion remains 0/3, while its approved partial behavior passes 3/3. No ingredient or allergy rule was relaxed. |
| **R4 — selected guests** | **Implemented; endpoint/workflow coverage completed** | The HTTP matrix covers omitted/empty defaults, guest-only, household plus guest, inactive/unknown rejection and duplicate ids across generation, create/update and acceptance. Rejected updates preserve existing attendance. PostgreSQL workflows verify explicit guests through generation/acceptance, shopping quantities, changed reviews and deactivation. The real browser verifies quick-plan household defaults. [Completion report](local-workflow-coverage-2026-11-12.md). | Per-day attendance composition remains a deferred product feature. Quick plan currently uses household defaults; it has no separate guest-selection interface. No remaining reproduced R4 defect was found. |
| **R5 — late frontend reads** | **Implemented for the identified paths** | Sequence guards protect week navigation, recipe filtering and shopping recipe search. AI statistics now cancels the previous refresh and unsubscribes on destruction, closing the followup's additional ordering concern. [Week](../../src/dishhive-web/src/app/pages/week-planner/week-planner.page.ts#L147), [recipes](../../src/dishhive-web/src/app/pages/recipes/recipes.page.ts#L114), [shopping search](../../src/dishhive-web/src/app/pages/shopping-list/shopping-list.page.ts#L112), [statistics](../../src/dishhive-web/src/app/pages/ai-planning-stats/ai-planning-stats.page.ts#L38). | Week/filter/statistics response-ordering tests pass today. Shopping search has a source guard but no equivalent page regression. No remaining reported R5 defect was found. |
| **R6 — reviewed acceptance and stock** | **Completed locally** | Durable reviewed snapshots, idempotent acceptance and PostgreSQL reservation locks remain covered. Freezy snapshots now distinguish unavailable, missing, shortage, changed and unverified stock. New batches bind original freezer details; acceptance rejects changes. The planner offers review, edit, confirm and unlink actions while retaining meals. Concurrent reassignment rejects stale writes. [Implementation and evidence](persistence-contracts-fix-2026-11-13.md). | Reconciliation is an observation at its displayed check time; independent Freezy changes can happen afterward. Legacy reservations require explicit review. |
| **R7 — AI execution guarantees** | **Implemented; planning quality gated** | The executor enforces configuration and planning eligibility. Format support is separate from required planning/intent quality; only explicit internal probes bypass that verdict. Every model-backed feature uses shared validated execution with exactly one terminal semantic outcome. Failed quality uses cooldown; successful evidence expires; shared probes observe host lifetime. [Completed job](execution-semantics-fix-2026-11-12.md). | Capability evidence covers finite planning examples. Facts now has an independently labelled regression corpus; its finite scope and current results are recorded separately. Translation/extraction quality still needs independent corpora. Served-model changes behind an unchanged identifier still require manual retesting. |
| **R8 — budgets and scheduling** | **Implemented; token admission remains conservative** | One deadline/id/provider-attempt counter spans intent, nested research extraction/facts, generation and corrections, including parallel work. SDK retries are disabled; gate-owned retries/backoff consume that allowance and release admission while waiting. Real SDK regressions cover 429/503, retry limits, cancellation and deadlines. All 27 live operations passed the ordinal audit. [Completed job](execution-semantics-fix-2026-11-12.md). | Input admission is an estimate rather than model-tokenizer proof. Recorded attempts identify admitted provider-client calls; network failure does not prove remote receipt. Shared capability probes have their own operation budget while each caller's wait stays bounded. |
| **R9 — meaningful telemetry** | **Completed locally** | Query filtering remains fixed. New calls expose operation input versions/hashes; structural review events distinguish submitted edits from changed recipes, household requirements, collections and freezer stock. Same-source facts corrections, later source edits and text edits after translation are separate. Retries retain their history; cohorts use recorded operation links. 595 backend tests, 67 frontend tests, 24 browser checks and nine Linux tests pass. [Implementation/evidence](review-telemetry-fix-2026-11-13.md). | Review signals do not measure independent dietary/translation/planning accuracy. The user has approved live evaluation with that payload/destination; results are recorded in the classification/retrieval report. |
| **R10 - regression gates and evaluations** | **Local gates and semantic/policy coverage implemented; live gaps visible** | Complete local command passes 683 backend tests with zero skips, 84 frontend tests, 28 fixture browsers plus one actual API/Chrome workflow, one production PWA lifecycle, nine Linux scraper tests and offline oracle checks. Facts pass 150/150, identity 69/69 and clarified Friday policy 3/3. [Current evidence](dish-identity-fix-2026-11-14.md). | Strict full-corpus completion is 17/27; accepted partial behavior is reported separately. Broader translation/extraction corpora remain separate followups. Finite model evaluations do not guarantee future completions. |
| **R11 - UI consistency** | **Completed locally for the reviewed surfaces** | Material collection selection/removal/create controls and course controls; native rich quick-plan date buttons retained after review. Keyboard selection, internal scrolling and focus return are covered. English/Dutch long-content screenshots were inspected at 360px and 1280px; PWA banners also at light/dark. [Decisions and evidence](local-operations-fix-2026-11-13.md). | User visual review can now iterate on the rendered output. No remaining reproduced R11 defect is known. |
| **R12 — contracts and application services** | **Completed locally** | Every frontend API service adopts generated Angular clients; wire DTOs/enums are reused. Unique operation IDs, accurate required fields, upload schemas and pagination are generated and checked for drift locally. Recipe writes are extracted into a cohesive application service. HTTP conflicts/errors preserve status, stable code and trace ID. Relational and actual API/browser workflows pass. [Implementation and evidence](persistence-contracts-fix-2026-11-13.md). | Continue maintaining these local checks as endpoints change. |
| **R13 — deployment trust boundary** | **Trusted-local acceptance implemented; remote deployment outside scope** | Compose binds app/database/scraper/search to loopback. REST and read-only MCP use distinct credentials; body, rate and expensive-operation concurrency limits exist. A remote overlay requires credentials/unique database password and disables demo seeding. [Security boundary](../../src/Dishhive.Api/Services/Security/ApiSecurityOptions.cs), [Compose](../../docker-compose.yml), [remote overlay](../../docker-compose.remote.yml), [guidance](../deployment-security.md). | Remote access still depends on an operator-supplied authenticated TLS proxy. No integrated user/role authorization profile or verified remote deployment is provided. The user has selected local-only development. A future remote/shared deployment needs separate external-protection evidence. |
| **R14 — recoverable scraper updates** | **Implemented; nine fresh Linux tests passed** | Validated versions, serialized updates, staged installation/smoke checks, atomic activation, previous-version rollback and startup reconciliation exist. The lock-open and post-activation-failure regressions are permanent. [Implementation](../../src/dishhive-scraper/app.py#L125), [recovery tests](../../src/dishhive-scraper/test_recovery.py). | Rehearse a real pinned-image/package-update rollback before release. The current extraction/update/recovery suite passes in the rebuilt Linux image; this audit did not update a real scraper package. No remaining reproduced R14 defect was found. |

R6 also now revalidates required collection membership by stable id during acceptance,
with a PostgreSQL regression for membership removed after review. R10 includes the two
new manual/automatic collection scenarios with actual member-id assertions and paced
requests. Freezy reconciliation is implemented and verified with controlled stock snapshots; a passing full live planning gate remains open.
The queued attendance matrix and real API workflow are now covered by the completed
local endpoint/workflow job; broader family/shopping workflows remain separate.

**The ten crosscheck defects are repaired, not ten entire review packages completed**

| September crosscheck defect | Current disposition |
| --- | --- |
| 1. Human confirmation overwritten at worker commit | Confirmation version is in the database concurrency condition; PostgreSQL regression retained. |
| 2. Pre-intent exception returns unrestricted complete fallback | Explicit unresolved-intent result; current regression passes. |
| 3. Rules fallback loses guests and candidate versions | Shared normalization plus end-to-end PostgreSQL regressions retained. |
| 4. Reviewed facts/reservation identity can change unnoticed | Durable review snapshot, identity comparison and current compatibility revalidation; PostgreSQL regressions retained. |
| 5. Enrichment lease expires after startup and remains stranded | Recovery on every poll, guarded ownership and coalescing; PostgreSQL regressions retained. |
| 6. Translation chunks exceed gate admission budget | Shared sizing/envelope/headroom; current regression passes. |
| 7. Concurrent freezer retry falsely returns unavailable | Idempotency rechecked under stock lock; PostgreSQL regression retained. |
| 8. Dessert/wrong-slot rows satisfy dinner requirements | Final date/meal/course/coverage checks; current regressions pass. |
| 9. Invalid browser fixtures hide broken pages | Typed fixtures, unexpected-request/runtime-error failures, semantic assertions; 17 fresh browser passes. |
| 10. Scraper update leaves lock/activation inconsistent | Cleanup/rollback/startup recovery; nine fresh Linux tests passed. |

**Historical v4-flash followup (13 November)**

The earlier v9 image scored 23/27: one source outage and three unfilled Friday requests. Facts scored 150/150 with low reasoning and 2048 tokens on v4-flash. Those are historical model/configuration results. The 14 November semantic checks and approved v4.1-flash measurements above supersede them.

**Historical planning baseline (12 November)**

The [initial fresh evaluation](../../artifacts/review-status-2026-11-12/rerun/live-planning-3x.json) was **3/21** including the known limitation. The completed R3 job then ran the expanded corpus three times per scenario with `deepseek/deepseek-v4-flash` through OpenRouter and Freezy disabled. Results depend on facts readiness: the initial expanded run was **3/27** with unknown facts and four rate-limited requests; after pacing and AI assessment it was **3/27** without transport errors because every original recipe conflicted with a fictional member's allergy under those assessments. Those historical classifications were unverified at the time. The 13 November refresh now passes minimum ingredient assertions for all 20 stored public recipes; unjudged extra classes and unspecified product composition remain outside that evidence.

With **four labelled synthetic positive-control recipes and explicitly confirmed canonical facts**, the 12 November full run was **15 passed / 12 failed across 27 requests** (**15/24 active**, known limitation **0/3**). No assertions or household allergies were weakened. This is a different dataset, not a controlled classifier improvement. Summed request duration was **369.633 seconds**; there were no transport failures or advisory overruns. [Complete results, configuration and remaining gaps](planning-constraints-fix-2026-11-12.md).

| Scenario | Last result | What remains |
| --- | --- | --- |
| Distinct vegetarian source | 0/3 | Too few valid distinct source recipes; partial/unable plans. |
| Multiple sources, desserts, chicken slot | 0/3 | Required source counts/courses/slots remain unfilled; model/repair outputs and candidate availability both contribute. |
| Dutch distinct source | 0/3 | Incomplete source coverage; some returned dishes require facts review. |
| Local recipes stay linked | 3/3 with positive controls | Complete, linked, unique results without allergy-review warnings. Original-only assessed data has no eligible local recipes. |
| Quick local recipes | 3/3 with positive controls | Complete, linked results within the time ceiling. Dataset eligibility remains essential. |
| Contradictory hard requirements | 3/3 | Correctly unable in these runs; this is measured behavior, not a guarantee of language interpretation. |
| Mandatory saved collection | 3/3 with positive controls | Actual member recipe ids required; complete linked results. Final image repeat also passed 3/3. |
| Mandatory quick auto collection | 3/3 with positive controls | Actual member recipe ids and time ceiling required; final image repeat also passed 3/3. |
| Dutch Friday pancakes | Known limitation, 0/3 | Explicitly run this time; all partial, requested Friday source main absent. Still not solved. |

The 12 November [execution followup](execution-semantics-fix-2026-11-12.md) reproduced **15/27** on the same 24-recipe dataset. Its 117 provider calls across 27 operations have contiguous ordinals, no lingering unvalidated outcomes and a maximum of 11 calls against the shared limit of 32. This closes attempt accounting; it does not close the source/facts quality gaps above.

Known-allergy rejection and visible partial results are progress. They do not establish that the planner can produce the complete requested week. Conversely, an unverified-facts warning is not proof that an actual allergen was present. Keep requirement preservation, candidate/facts readiness, and generation quality as separate iteration targets. Preserve the existing assertions.

**Additional proposals and deferred features**

| Proposal from the original review | Current status | What is still needed |
| --- | --- | --- |
| Per-day attendance, including guests | **Not implemented as proposed** | Overall attendee selection/policy exists; day-level composition and inference from existing plans remain future work. |
| Explicit requirements and partial-plan explanation | **Implemented; source availability remains measured separately** | Collection/slot/count/date/facts/identity invariants and explicit dated gaps are implemented. Independent identity tests and URL labels replace source/date-only success. Finding enough eligible source recipes remains a retrieval-quality limitation, detailed above. |
| Reviewable AI enrichment | **Partial** | Facts state/history/revisions and original text are available. Full proposal review, evidence/model provenance and a per-recipe retry workflow are not established as complete. |
| Persisted shopping check-off/manual items | **Not implemented** | Shopping remains a computed list/export; define stable item identity, persistence and cross-device behavior. |
| Ingredient canonicalization/unit normalization | **Partial existing foundation** | Existing unit conversion/aggregation is useful; editable canonical ingredient mappings and safe cross-variant merging remain future work. |
| Move/swap meals with undo | **Not implemented** | Dedicated move/swap/undo workflow preserving links, attendees and reservations. Editing a date is not the full proposed workflow. |
| Cooking mode | **Not implemented** | Step-focused workflow, timers/wake lock and supporting acceptance evidence. |
| Per-task model routing/cost limits | **Not implemented as proposed** | Task profiles exist, but configurable model routing/cost limits and operation progress/cancellation UX are not complete. |
| Full backup/restore/migration rehearsal | **Completed locally** | All 25 tables, images and migrations match the restored clone; candidate app startup and APIs pass. [Recovery evidence](local-operations-fix-2026-11-13.md). Environment secrets/external services are separate. |
| Household clock/time zone | **Implemented and tested** | Configured household zone and injectable server/browser clocks; midnight/DST/cross-zone checks. UTC event timestamps retained. |
| Complete application vocabulary localization | **Implemented for application-owned vocabulary** | EN/NL templates, notifications, paginator, canonical labels and deterministic planning issues audited locally. User/source names, categories and units retain their supplied content. |
| Production PWA freshness/cache verification | **Completed locally** | Real worker tests cover HTTP outage while online, visible saved-data time, offline reload, writes/images/cache invalidation, recovery and actual app-version reload. [Evidence](local-operations-fix-2026-11-13.md). |
| Updated project guidelines | **Implemented** | Guidelines now describe Amber/Cyan, the shared multi-task gate, durable revision-bound enrichment and generated-contract drift checks. |

The original sequence explicitly deferred attendance UX, shopping workflow, meal movement and cooking mode until after correctness. They should remain visible as outstanding features, without being presented as regressions in the repaired work.

**Review the implemented output in this order**

These are manual acceptance steps for the rebuilt local development instance. The existing browser suite uses fixtures; the additional production smoke covers real read-only pages/offline shopping, not all mutation journeys. Use disposable data for concurrent writes, scraper updates and recovery rehearsals.

- [ ] **Recipe detail/edit:** confirm a recipe's classes, add a conflicting ingredient, and save with AI unavailable. Expect needs-review/stale rather than current confirmation. Explicitly reconfirm and check the new state. Review original/current text and the pending/failed labels.
- [ ] **Attendance:** generate a plan for household plus an active guest, then for a guest only, and accept it. Check the saved attendance and shopping quantities. Inspect the default household selection separately.
- [ ] **Navigation/search:** rapidly change weeks/recipe filters and AI-task filters. The selected context and displayed results must stay aligned; existing controlled-order tests cover these races.
- [ ] **Suggestion review:** submit a mixed batch where one item succeeds and another import fails. Confirm the successful item is not duplicated on retry and unselected items remain available. For changed saved facts/attendees/content, expect `needsReview` and a route to the retained recipe.
- [ ] **Partial plans:** request an impossible or undersupplied combination of source/count/time requirements. Review explicit unmet requirements and whether partial results are useful. A successful HTTP request or model call is not a complete-plan pass.
- [ ] **Collection requirements:** select two dinner dates and request `Only use #[Easy Weekday Dishes].` Check linked membership, then try the quick auto collection and an unknown reference. Remove a required member after generation and expect `needsReview` on acceptance. The four **Evaluation fixture** recipes provide eligible synthetic review data; their confirmed facts do not validate the original recipes' AI assessments.
- [ ] **UI:** review populated collection chips, long labels and quick-plan dates in both languages at mobile/desktop widths. Keyboard/scroll/focus behavior is verified; see [rendered evidence](local-operations-fix-2026-11-13.md).
- [x] **Fresh local evidence:** the initial database was cleared and migrated; the final complete run passes all 683 backend tests, including PostgreSQL, plus nine Linux scraper tests. Current source is deployed.
- [x] **Local recovery evidence:** full database/images restore and candidate migration/startup rehearsal passed. External deployment is outside the local acceptance scope.

**Next iteration queue**

| Order | Work | Exit evidence |
| --- | --- | --- |
| **Completed** | **R9 metrics query bug** | Unset parameters omitted; HTTP/browser regressions fail before the fix and pass afterward; deployed real counts 88 → 40 → 88 at mobile and desktop widths. [Completion evidence](metrics-query-fix-2026-11-12.md). |
| **Completed** | **R3 collection IDs, candidate selection and facts-gap diagnostics** | 505 backend passes including PostgreSQL; complete three-iteration live corpus, recorded datasets and remaining failures. [Completion evidence](planning-constraints-fix-2026-11-12.md). |
| **Completed** | **R4/R10 local endpoint/workflow coverage** | Full attendance matrix, real API/browser import/edit/quick-plan/generate/accept journey, 538 backend passes including PostgreSQL, 18 fixture browser passes and nine Linux scraper passes. [Completion evidence](local-workflow-coverage-2026-11-12.md). |
| **Completed** | **R7/R8 execution semantics and provider attempts** | 562 backend passes, 20 fixture browser passes, shared validated execution and strict planning eligibility; actual SDK retry regressions plus a 117-call/27-operation live audit. [Completion evidence](execution-semantics-fix-2026-11-12.md). |
| **Completed** | **R6/R9/R12 persistence, contracts and review telemetry** | Generated clients, recipe mutation service, consistent errors, freezer reconciliation, structural review history and input versions. 595 backend tests, zero skipped; 67 frontend tests; 24 browser checks; nine Linux tests. [Contracts/stock](persistence-contracts-fix-2026-11-13.md), [telemetry](review-telemetry-fix-2026-11-13.md). |
| **2 - partial** | **Source discovery and independently measured facts** | Facts pass 150/150, identity 69/69 and the clarified Friday policy 3/3. Count/date handling, food approvals, source scope and dated partial behavior are repaired. Full strict planning is 17/27; six English/mixed-source repeats remain incomplete and one Dutch source outage follows accepted partial behavior. [Exact evidence](dish-identity-fix-2026-11-14.md). |
| **Completed** | **Local UI and operational verification** | Material/control decisions and inspected EN/NL mobile/desktop output; production PWA outage/update/write/image tests; household clock and catalog audit; complete local restore. [Report](local-operations-fix-2026-11-13.md). |
| Later, by product priority | Deferred attendance/shopping/canonicalization/movement/cooking/routing features | A separate domain contract and user acceptance evidence for each feature. |

The historical raw artifacts are ignored by Git. The checklist above and the checked-in regression sources are the durable review record; retain the linked artifacts separately if sharing the evidence.
