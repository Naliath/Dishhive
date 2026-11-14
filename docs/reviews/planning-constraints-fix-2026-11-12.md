**Completed job: collection identity and constrained candidate selection — 12 November 2026**

The second implementation job is complete and deployed at **http://localhost:5100**.
Required collection membership now uses stable ids, candidate selection applies the final
domain invariants before choosing rows, and missing facts have their own diagnostics.
The whole AI planning product is still incomplete: source discovery and facts accuracy
remain separate iteration targets. The [current review checklist](application-review-status-2026-11-12.md)
records both the completed implementation and those outstanding outcomes.

The deployment uses image
`sha256:09b7c4814aea7a80b69f8be0bca16258b9a3ce9385117d219bbb7b53d57a4caf`.
The existing uncommitted/untracked implementation remains in the working tree; this job
does not create a commit. No database migration was needed for these changes.

**What changed**

- [Collection resolution](../../src/Dishhive.Api/Services/Suggestions/CollectionMentionResolver.cs)
  retains complete recipe-id membership and stable manual/automatic collection identity.
  Only the displayed prompt titles are sampled, at 15 per collection. Meal/course/date
  scopes are distinct, and references on unselected days are ignored for that request.
- [Intent v4](../../src/Dishhive.Api/Services/Suggestions/PlanningIntent.cs) carries required
  collection ids and explicit soft preferences. Every resolved reference must survive
  interpretation. Corrective attempts receive bounded field-specific validation errors,
  including missing source queries. Unknown/deleted/disabled collection references return
  an explicit `collection-unresolved` unable result without calling the provider.
- [Candidate selection](../../src/Dishhive.Api/Services/Suggestions/PlanningCandidateSelector.cs)
  considers resolved proposals, all local recipes, verified research candidates and freezer
  stock. It uses the [shared final-item validator](../../src/Dishhive.Api/Services/Suggestions/PlanningIntentValidator.cs)
  for identity, membership, origins, time, facts, allergies, attendees and slot constraints.
  Bounded backtracking then checks counts, distinctness, repeated dishes, freezer limits
  and stock together. It does not cycle one recipe to satisfy a distinct-count request.
- [Research provenance](../../src/Dishhive.Api/Services/Suggestions/ExternalRecipeTools.cs)
  survives shared cached queries. A dessert candidate cannot silently fill a main-course
  requirement on the same host. Post-processing also distinguishes meal/course slots.
- [Finalization](../../src/Dishhive.Api/Services/Suggestions/LlmMealSuggestionService.cs)
  reports the actual remaining gaps. Unknown hard-required facts produce `facts-*`
  diagnostics; a successfully repaired model proposal no longer adds a generic
  `model-quality` product failure. Raw completion validation outcomes remain recorded.
- [Acceptance](../../src/Dishhive.Api/Services/Suggestions/MealSuggestionAcceptanceService.cs)
  resolves required collections again by stable id and checks current membership.
  Manual membership rows are locked through the transaction. A renamed collection keeps
  its identity; removing the selected member produces `needsReview`.

Selection searches at most 25,000 assignments. An incomplete bounded search is not proof
that no feasible assignment exists. Interpretation, fetched recipe correctness and facts
classification remain model-dependent; domain invariants do not establish their accuracy.

**Verification**

| Check | Result |
| --- | --- |
| Backend including PostgreSQL migrations/concurrency | **505 passed, 0 failed, 0 skipped**; [TRX](../../artifacts/planning-constraints-2026-11-12/backend-final.trx). |
| Angular unit tests | **64 passed across 16 files**; [log](../../artifacts/planning-constraints-2026-11-12/frontend-tests.log). |
| Browser fixture typecheck and Playwright | **Passed; 18 browser tests passed** with retained screenshot baselines; [log](../../artifacts/planning-constraints-2026-11-12/browser-tests.log). These use API fixtures. |
| API generation | **Passed**; [log](../../artifacts/planning-constraints-2026-11-12/api-generation.log). |
| Production image build | **Passed**, including the Angular production build; [log](../../artifacts/planning-constraints-2026-11-12/final-build.log). |
| Model capability | **8 checks passed**, JsonSchema, 10.772 seconds; [result](../../artifacts/planning-constraints-2026-11-12/capability.json). This probe is not the live quality oracle. |
| Final-image collection repeats | **6/6 passed**: manual and automatic membership, three iterations each; [report](../../artifacts/planning-constraints-2026-11-12/final-image-collections-3x.json). |
| Final-image unknown-reference API checks | **3/3 explicit unable results**; [responses](../../artifacts/planning-constraints-2026-11-12/final-image-unresolved-3x.json). A unit regression separately verifies zero model calls. |
| Restored development deployment | **Healthy**, same image; AI, Freezy, scraper and search reachable. Diagnostic-text retention restored to `false`. [Health](../../artifacts/planning-constraints-2026-11-12/restored-health.json), [settings](../../artifacts/planning-constraints-2026-11-12/restored-deployment.json). |
| Diff / document checks | Scoped code diff and new-file whitespace checks passed; documentation links resolve. Full-tree diff check reports only the pre-existing trailing space in `possible-features.md:55`, left untouched. |

Permanent regressions cover same-title impostors, full membership beyond the title sample,
global requirements, exact main/dessert scope, renamed collections, changed membership at
acceptance, scarce candidates under combined restrictions, uniqueness, unknown facts,
shared research queries, unresolved references and unselected-day instructions.
See [candidate tests](../../src/Dishhive.Api.Tests/Services/PlanningCandidateSelectionTests.cs),
[intent tests](../../src/Dishhive.Api.Tests/Services/PlanningIntentContractTests.cs),
[collection tests](../../src/Dishhive.Api.Tests/Services/CollectionMentionResolverTests.cs),
[HTTP tests](../../src/Dishhive.Api.Tests/Integration/MealSuggestionsIntegrationTests.cs), and
[PostgreSQL acceptance tests](../../src/Dishhive.Api.Tests/Crosscheck/PersistenceCrossTests.cs).

**Live results and dataset limits**

The expanded [corpus](../../src/Dishhive.Api.Tests/Fixtures/AiWeekPlanning/scenarios.json)
contains eight active scenarios plus one known limitation. Each ran three times against
the local Compose API using `deepseek/deepseek-v4-flash`, OpenRouter, SearXNG and the
typed v4 intent contract. Freezy was disabled during every live evaluation so model inputs
were synthetic. The runner now spaces requests by at least seven seconds to respect the
API limit. New collection assertions fetch full recipe-id membership from the real API;
the existing allergy, source, uniqueness, course, date and attendance assertions remain.

| Dataset / phase | Pass / fail across 27 requests | Interpretation |
| --- | --- | --- |
| Original 20 demo recipes, unassessed facts | **3 / 24** | Unknown-facts warnings remained; four requests also hit the API rate limit before runner pacing was added. [Report](../../artifacts/planning-constraints-2026-11-12/live-unassessed-3x.json). |
| Original 20 recipes after AI facts assessment, paced | **3 / 24** | No transport failures. All 20 were classified as conflicting with the fictional household's allergies (19 Milk, one Shellfish), leaving no eligible local recipe. These classifications were not independently verified. [Report](../../artifacts/planning-constraints-2026-11-12/live-assessed-3x.json), [readiness](../../artifacts/planning-constraints-2026-11-12/facts-readiness.json). |
| Same household plus four explicit positive controls, paced | **15 / 12** | Feasible local and collection requests pass. The added recipes change feasibility; this is not evidence that the classifier improved. [Report](../../artifacts/planning-constraints-2026-11-12/live-positive-3x.json). |

The four controls are tagged **Evaluation fixture**: lentil bowl, chickpea rice, carrot
soup and chicken rice, with simple ingredients, recorded preparation times and explicitly
confirmed canonical classes. The first three are also in **Easy Weekday Dishes**; all four
qualify for the quick auto collection. They remain in the local development database for
manual review. Original recipe facts and all fictional household allergies were retained.
The [idempotent fixture script](../../artifacts/planning-constraints-2026-11-12/add-positive-fixtures.ps1)
and [manifest](../../artifacts/planning-constraints-2026-11-12/positive-fixtures.json) record their inputs and ids.

| Scenario, positive-control dataset | Passes | Remaining outcome |
| --- | --- | --- |
| Local recipes stay linked | **3/3** | Complete, linked, unique plans without allergy-review warnings. |
| Quick local recipes | **3/3** | Complete, linked plans inside the time ceiling. |
| Mandatory manual collection | **3/3** | Actual member ids, complete plans; final-image repeat also 3/3. |
| Mandatory quick auto collection | **3/3** | Actual member ids and time ceiling; final-image repeat also 3/3. |
| Contradictory hard requirements | **3/3** | Explicit unable result is the expected success. |
| Distinct vegetarian source | **0/3** | One valid distinct source main instead of four. |
| Multiple sources, desserts and chicken | **0/3** | One source main instead of four; required desserts absent. |
| Dutch distinct source | **0/3** | One valid distinct source main instead of four. |
| Dutch Friday pancakes, known limitation | **0/3** | Search returned no candidate for the requested Friday main; unrelated rows still need facts review. |

The positive full run had **15/24 active passes**, plus **0/3** for the known limitation;
**zero transport errors** and **zero advisory overruns**. Summed HTTP duration was
369.633 seconds. Stored planning operations averaged 13.669 seconds, P95 42.023 seconds;
completion time averaged 10.801 seconds. They recorded 15 searches, three empty searches,
72 recipe resolutions, 12 resolution failures and zero parse failures. The 118 correlated
model calls comprise 105 success and 13 validation-failure outcomes; call success is not
the same as a complete correct plan. [Metrics](../../artifacts/planning-constraints-2026-11-12/positive-metrics.json),
[all correlated calls](../../artifacts/planning-constraints-2026-11-12/positive-calls.json),
[logs](../../artifacts/planning-constraints-2026-11-12/positive-app.log).

The complete 27-request run used the core implementation image; the final image adds the
unknown-reference guard and selected-day input scoping. Both collection cases were then
repeated on that final image. Normal development settings, including Freezy and disabled
diagnostic-text retention, were restored after the synthetic evaluations. The database
was retained. These runs generated review batches; they did not accept model output as meals.

**Review and next work**

At **http://localhost:5100**, select two dinner dates and request
`Only use #[Easy Weekday Dishes].` Review that both rows link to actual collection members.
Try `Only use #[Quick (max 30 min)].` and inspect the linked recipes and time limits.
An unknown collection should display an explicit unmet requirement. For a reviewed
required-collection row, remove its membership before accepting: expect `needsReview`.

The subsequently completed [R4/R10 local endpoint/workflow job](local-workflow-coverage-2026-11-12.md)
adds the attendance matrix and real API/browser journey. The next checklist item is
**R7/R8 execution semantics and attempt accounting**.
Source discovery and independent facts-quality evaluation also remain visible work:
the full live planning gate is red. No source-count assertion was relaxed and the
Friday-pancakes case remains an explicitly runnable known limitation.

Raw artifacts are ignored by Git. Keep this report, the maintained corpus and permanent
regressions as the durable record; retain the linked artifacts separately when sharing evidence.
