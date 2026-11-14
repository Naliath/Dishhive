# Final source and facts verification - 13 November 2026

Historical verification. The [14 November dish-identity followup](dish-identity-fix-2026-11-14.md)
adds the semantic contract, independent food oracle and approved v4.1-flash results.
The counts and unresolved implementation statements below describe the earlier image.

This followup records the last numbered review job after the user's clarification of
partial-plan behavior. The [current checklist](application-review-status-2026-11-12.md)
is the item-by-item review entry point. Deferred cooking, movement/undo, shopping
persistence, day-level attendance and model-routing features remain separate work.
Development and acceptance checks run locally; no hosted CI was added.

**The last job is still partial.** Facts accuracy and count/date handling are repaired,
but the final strict planning score is 23/27. The Friday requests expose a food-identity
gap that earlier source/date-only passes failed to detect. Those old scores are not
pancake completion evidence. The implementation and oracle work still needed is
explicit below.

## Clarified behavior

The user selected: **"Leave those dates empty and mark the plan partial"** when the
selected week cannot be filled with distinct assessed meals. The planner now rejects
unassessed dishes for attendees with canonical allergy exclusions, including plain
dishes and freezer choices without assessed ingredient data. It retains valid meals
and returns partial coverage with localized explanations naming the missing dates.
It does not repeat dishes, invent ingredient facts or loosen a requested source,
weekday, course, count or allergy rule to fill the week.

The Friday scenario still uses its original Dutch prompt, all seven selected dates
and its source/weekday/uniqueness/allergy assertions. Its expected status is now
`partial`, and an additional assertion requires all four eligible saved control
recipes. This is an explicitly authorized product-policy change to the expectation,
not evidence that a complete seven-day plan has become possible. The expected result
on this dataset is the assessed Friday source meal plus four saved meals, leaving
two dates empty. The case is active rather than skipped as a known limitation.

## Independent facts

| Measurement | Result |
| --- | --- |
| Expanded corpus | Same 50 cases, three repeats; labels unchanged |
| Prior reasoning-disabled run | 149/150; one missed Crustaceans in mixed shrimp paste |
| Focused reasoning run | 18/18 across six difficult cases |
| Complete reasoning run | **150/150**, zero failed logical assessments |
| Physical calls in complete run | 151: 150 success, one parse failure recovered by the existing retry |
| Logical assessment latency | Average 4.201 seconds; P95 13.660 seconds |
| Tokens in complete run | 237,246 total, including 54,581 reasoning tokens |

Both comparisons use `deepseek/deepseek-v4-flash` at the already approved OpenRouter
destination and retained facts contract `recipe-facts-v7`. The complete benchmark's
corpus SHA-256 is
`91A9CCE05787DCEAFBE2C08DA05BA460BB3EB6C7DAE6EE25AE3F0C2B10332E9B`.
The final setting enables low reasoning through the central execution gate and
allows 2048 output tokens for facts only. It does not enable reasoning for planning,
translation or extraction. Raw reasoning is not returned to the user; token usage
and effective reasoning settings are included in task evidence. OpenRouter documents
the model's [supported parameters](https://openrouter.ai/deepseek/deepseek-v4-flash)
and [reasoning controls](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens).

The standalone benchmark uses prompted JSON. The deployed application uses verified
schema support where available; these are not controlled comparisons of response
modes. Exact cases judge complete class sets, while minimum cases judge only their
specified assertions. The 150 passes do not establish arbitrary ingredient accuracy,
unspecified prepared-product composition, cross-contact safety or future consistency.
The 20 stored public recipes already pass their minimum assertions after the previous
v7 refresh; this followup does not overwrite their data or human confirmations.

Evidence: `artifacts/final-item-2026-11-13/facts-reasoning-focused.json` and
`facts-reasoning-full.json`. The rejected prior per-ingredient output experiment
remains historical and is absent from the retained implementation.

## Retrieval and date interpretation

The `research-queries-v3` contract receives up to three eligible and three rejected
assessed examples, with titles capped at 160 characters and canonical class sets.
These examples help the model choose different useful food families after rejected
searches; title wording is never proof of ingredient facts. The same source hosts,
typed requirements and ingredient validator remain authoritative.

When facts reasoning is enabled and only one research requirement remains, bounded
four-page waves avoid the previous serial tail. Fair allocation across simultaneous
requirements, the **28-distinct-page ceiling**, generation reserve and shared
**32-provider-attempt / 300-second** operation limits remain in force. Extra assessed
candidates do not increase the final requested meal count.

The initial complete run with these changes scored **26/27** under the existing
oracle. The Friday source/date passes are overstated as food-identity evidence:
later inspection found unrelated meals in those slots, as explained below.
One mixed-source iteration failed after
3.455 seconds because the intent model assigned an undated vegetarian selection to
Friday, which was already explicitly reserved for chicken. That interpretation was
correctly rejected as conflicting, but it prevented the requested research.

Intent contract `planning-intent-v8` explicitly reserves named dates before
allocating undated counts. A conflicting first interpretation receives a bounded
correction within the existing intent retries and shared operation allowance. The
correction must retain every requirement and explicitly requested date. A final
valid interpretation that remains genuinely contradictory is still reported as
impossible. Controlled regressions cover corrected allocation, preserved explicit
contradictions and the shared provider-call limit.

The complete v8 repeat also reached **26/27**. All three mixed-source requests
passed; one Dutch request returned its four valid source meals but incorrectly
declared a partial plan. The interpreted constraint requested four selections while
listing all seven dates as required. Each listed date is a mandatory selection,
not an optional scheduling window, so those extra dates wrongly blocked saved
alternatives. The parser now rejects a nonempty date list whose distinct-date count
differs from the requested selection count. It returns a specific correction error
before research, preserving both explicit dates and the original count. Contract
`planning-intent-v9` documents that invariant; four regressions cover empty, too
short, exact and too broad date lists. The v8 run and its 273-call audit remain in
`planning-verified.json` and `intent-v8-audit/`.

The intermediate report is `planning-full.json` with `parallel-audit/`. Its 237
physical calls have contiguous ordinals, complete input metadata and terminal
outcomes; maximum 28 calls and 24 resolved pages. Its total request time is
967.900 seconds, with eight advisory overruns. The earlier focused Dutch run was
3/3 but took 517.148 seconds in total. These are diagnostic comparisons, not a
controlled latency benchmark: candidate choices, provider timing and local checks
vary between runs. Advisory budgets have not been raised or disabled.

## Final complete planning run

The complete final v9 corpus in `planning-counted-dates.json` scores **23/27**.
It takes **885.334 seconds** in total and reports **five advisory time overruns**.
No advisory threshold was raised or disabled.

| Scenario | Strict result |
| --- | --- |
| Distinct vegetarian source | 2/3; one external page-fetch outage |
| Mixed sources, courses and explicit chicken | 3/3 |
| Dutch distinct source | 3/3 |
| Local recipes stay linked | 3/3 |
| Quick local recipes | 3/3 |
| Conflicting hard requirements | 3/3 |
| Mandatory saved collection | 3/3 |
| Mandatory quick automatic collection | 3/3 |
| Dutch Friday pancakes | 0/3; four saved meals retained, Friday source meal missing |

One initial source repeat encountered **12 failed page fetches** among the 28
inspected candidates: four HTTP 500 responses and eight 15-second fetch timeouts.
It retained three assessed source recipes and left the remaining date empty. The
strict four-recipe oracle fails that iteration; it is not relabelled as a pass.
The user explicitly chose **"Keep partial; document the outage"** rather than add
network retries. This is accepted behavior under an external availability failure,
not proof that a fourth eligible recipe does not exist. Current limits and failed
evidence remain intact. Diagnostics identify operation `7a9d95f3`; the scraper
sidecar remained available, and the failed previews originated in page fetching.

**Friday remains open.** Its earlier source/date oracle failed to verify pancake
identity: the intermediate reports include vegan meatballs, tabouleh and a stew
with dumplings on Friday. Those are not successful pancake requests. The final v9
run returns four assessed saved meals and leaves Friday plus two other dates empty
in all three repeats. These Friday failures have **no page-fetch errors**: research
finds eligible ingredient-assessed candidates, but they do not establish the
requested food identity. The current implementation relies on optional intent
`dishRequest` and literal title containment. It can reject a spelling variant when
the field is present, or lose the requested food when it is absent. Query refinement
can also broaden pancakes into unrelated vegan mains.

The partial-plan and outage decisions do not authorize substituting unrelated food.
Reliable preservation/matching of a specific requested dish and an independent
food-identity assertion remain necessary before this Friday item can be closed.
The earlier 3/3 source/date scores must not be reported as pancake completion.

The final correlated audit covers **261 physical provider calls across 27 planning
operations**, with a maximum of 28 calls and 28 inspected pages. All provider
ordinals are contiguous, all calls retain input metadata and none remain without
a terminal outcome. Evidence: `verified-metrics.json`, `verified-audit.json` and
`planning-counted-dates-app.log`. This closes execution accounting, not the Friday
food-identity gap.

## Local verification and operating profile

The complete local command on the final counted-date source passes **668 backend tests, zero
skipped; 84 frontend tests; 28 fixture browser checks plus one actual API/Chrome
workflow; one production service-worker lifecycle test; and nine Linux scraper
tests**. Generated contracts, fixture contracts, localization and production
compilation pass. Evidence: `artifacts/final-item-2026-11-13/local-counted-dates/`.
English/Dutch integration checks verify that missing-dinner explanations contain
the correctly formatted date rather than an unresolved placeholder.

Generic checked-in defaults remain facts reasoning disabled, 512 facts output
tokens and model-call concurrency one. The measured development profile in the
local `.env` enables facts reasoning, 2048 facts output tokens and concurrency four.
Reasoning requires at least 1024 effective output tokens at validation time. These
settings are included in execution fingerprints and can be overridden explicitly
with `run-ai-facts-evals.ps1 -EnableFactsReasoning -MaxOutputTokens 2048`.

The development dataset remains 24 recipes (20 public AI-assessed recipes and four
labelled human-confirmed controls), four fictional members and 14 historical meals.
Human-confirmed recipe row hashes and canonical class sets match the pre-refresh
backup byte-for-byte. A fresh read-only comparison again passes all **20/20**
stored public minimum assertions (`stored-minimum-checks.json`). The household's canonical allergy exclusions are Milk,
Crustaceans and Molluscs. The separate public evaluation fixture has a different
allergy profile and is not substituted for this dataset.

The final-image restore rehearsal passes: all **25 tables**, complete row/image
digests, 24 recipes, four members and 14 meals match the disposable restored clone.
The exact image boots with migrations and no persisted data changes. Its immutable
identity is `sha256:e6b9ee9ca8b69ff5ff9b640ef2253e7fae04f0bce2ce7ee3ab8f82274c6d2597`.
Evidence: `restore/summary.json`, `tables.json` and the retained database dump.
Normal Freezy integration, background jobs and disabled durable diagnostic-text
retention are restored and the final image is healthy at http://localhost:5100.
The measured facts reasoning/output/concurrency settings remain enabled locally.
Evidence: `normal-deployment.json`.

The actual deployed recipe/collection and quick-plan screens pass read-only Chrome
checks at **360px and 1280px**, including geometry, runtime errors and keyboard
closure. All four captures were visually inspected. Evidence: `deployed/summary.json`,
`collections-360.png`, `collections-1280.png`, `quick-plan-360.png` and
`quick-plan-1280.png`. Opening these screens does not save meals or recipe changes.

## Review the partial-plan output

1. Open http://localhost:5100 and move to an empty future week.
2. Choose Suggest week, select all seven dates and enter the unchanged regression
   prompt: `Op vrijdag pannekoeken van @[Dagelijkse Kost] als hoofd maaltijd`.
3. The current v9 result retains four eligible saved meals and leaves Friday plus
   two other dates empty, naming the missing dates. A future resolved result must
   establish that the source meal is actually pancakes before retaining it on
   Friday. Source availability can reduce coverage further; the planner must
   disclose those gaps instead of inventing, substituting or repeating meals.

The [main review checklist](application-review-status-2026-11-12.md) retains the
broader manual acceptance steps and the separate deferred feature list.
