# Source discovery and independent facts - 13 November 2026

**Historical run record.** The later [final source/facts followup](final-source-facts-closure-2026-11-13.md)
records the user's partial-plan decision, task-specific facts reasoning, additional
retrieval/date corrections and final verification. The results below retain their
original settings and expectations; they are not the current completion status.

This report separates independent classification accuracy, retrieval/eligibility,
and complete planning results. Passing local tests does not establish any of those
live quality measures. See the [current checklist](application-review-status-2026-11-12.md)
for the latest disposition and remaining work.

## Independent facts benchmark

The new `Dishhive.Evaluations` console project runs the production
`LlmRecipeFactsExtractor` through `IAiExecutionGate`. Expected classes are checked
in separately from model output. Exact cases judge both missing and extra classes;
minimum-recall cases require explicit classes and only reject explicitly forbidden
extras. A missing completion can never pass as a verified empty class set.

The original 29 cases covered all 20 canonical classes, plant products, misleading
titles, shrimp paste and two mixed public recipes. Three repeats with the same
provider/model/configuration and unchanged labels produced:

| Contract | Passed / 87 | Reproduced failures |
| --- | --- | --- |
| v1 | 79 | Missing crustaceans and invented seafood categories |
| v2 | 85 | Missed celery in a mixed recipe |
| v3 | 85 | Untrusted title changed the answer |
| v4 | 86 | Missed soy in a mixed recipe |
| v5 | 87 | None in this finite run |

Original corpus SHA-256:
`B3EADE373A28075F2A9470C6DC7F5A257610AB937E7C5B1A2E8BC9501865BC75`.
Raw comparisons are in `artifacts/facts-accuracy-2026-11-13/`.

The public demo data then exposed gaps outside that small corpus: Dutch egg whites
were omitted, powdered sugar was confused with flour, and a veal cut was confused
with oysters. The corpus was expanded to **50 cases**, including minimum-recall
assertions for all **20 public demo recipes** and exact Dutch identity/declaration
cases. Its labels were assigned from ingredients before rerunning the classifier.
The unchanged expanded benchmark measured **136/150 for v5**, then **144/150 for
v6**, then **149/150 for retained v7**. One mixed shrimp-paste repeat still missed
Crustaceans. A per-ingredient output experiment regressed to 130/150 and was rejected.
This supersedes treating 87/87 as complete accuracy evidence. Minimum-recall
passes do not validate all extras or unspecified prepared-product composition.

Title text no longer reaches the provider's facts payload. The prompt requires
coverage of all ingredients without inferring from the title; the title remains in
local ownership/input-hash metadata. Prompt changes preserve the canonical taxonomy,
complete-declaration precedence and conservative treatment of unknown composition.
They do not add application-side word lists that assert ingredient facts.

`scripts/run-ai-facts-evals.ps1 -Iterations 3` reuses the local app's AI configuration
without copying database rows or printing credentials. `-ValidateOnly` checks the
corpus before creating a provider client. Reports include per-case results, missing
and extra classes, model/config/corpus hashes, timings, contracts and gate outcomes.
The standalone facts benchmark currently uses prompted JSON; its reports record
that mode explicitly. The deployed app can use previously verified schema mode,
so this is not a controlled comparison of those two response modes.
Expanded corpus SHA-256:
`91A9CCE05787DCEAFBE2C08DA05BA460BB3EB6C7DAE6EE25AE3F0C2B10332E9B`.

## Existing development assessments

After explicit approval for OpenRouter and the specified local context, all 20
AI-assessed recipes were refreshed through the durable worker. Four synthetic
human-confirmed controls were retained byte-for-byte, checked against a restored
pre-refresh backup. The first refresh changed eight class sets and preserved all 20 previous AI assessments in history. The final v7 refresh passes the checked-in minimum assertions for all 20 stored public recipes (20/20), while retaining four human-confirmed controls. This does not validate unjudged extras, unknown product composition or future model completions.

`POST /api/recipes/facts/backfill?includeAssessed=true` explicitly includes existing
AI assessments. The default endpoint behavior remains unchanged. Neither path
selects human-confirmed recipes. Before staging Pending, the worker archives the
prior valid assessment; completion does not replace it with a redundant Pending
history entry. Repeated identical class sets retain their separate assessment times. Controlled regressions prove history and human confirmations.

Evidence: `artifacts/facts-accuracy-2026-11-13/development-refresh/`, plus the full
pre-refresh dump in the [restore report](local-operations-fix-2026-11-13.md).

## Source discovery

The metasearch service responds, but its upstream engines currently return no usable
results because of rate limits/CAPTCHAs. The status UI now reports **Limited** while
retaining source research through the sitemap fallback. No CAPTCHA bypass was added.
Successful engine hits retain their ranking; sitemap hits only fill missing slots.

Research now shares candidate IDs and cached resolution tasks across repeated URLs.
It gives each requirement a research wave before spending the remaining allowance,
then stops when sufficient verified candidates exist. The original 24-page bound proved too small in a mixed-source repeat: 20 main
candidates plus four dessert candidates yielded three of four required mains.
Research now permits **28 distinct pages** while checking remaining provider
attempts and reserving the configured final-generation allowance. The shared
**32 provider attempts / 300-second operation** limits remain unchanged. The funnel distinguishes failed pages, unknown facts, excluded ingredients
and eligible candidates.

Sitemap relevance uses the recipe slug, rather than shared category paths. Rare
food terms receive inverse-document-frequency weighting. Known Dagelijkse Kost
recipe routes exclude questions, magazines and theme lists before they consume
the inspection budget. Unknown sites still require a successfully resolved recipe.
No title or route establishes dietary suitability. Unseen compound food terms are segmented only using words already present in the source index, allowing reversed/separated terms without a language-specific word list. Engine results outside the requested host cannot suppress the source fallback.

When verified ingredients reject the first wave, an optional `planning.research`
call refines the remaining searches using the canonical exclusions and rejected
classes. Its typed, strict `research-queries` contract permits up to three bounded
new queries. It uses the same operation ID, eligibility gate, provider-attempt
budget, telemetry and schema policy. If searches are exhausted and inspection capacity remains, one further refinement is allowed; the entire session is capped at four refinement calls. These are distinct retrieval steps, not transport retries. A malformed/empty optional refinement retains
the original fallback queries. Uninspected original hits are deferred, not lost.
The statistics page labels this task in both languages.

## Planning comparisons and boundaries

The original development dataset has 20 public demo recipes plus four explicitly
confirmed synthetic controls, four fictional members and 14 historical meals.
Household allergies and corpus assertions were not weakened. The v7 intent/full
planning run after the first facts refresh remained **15/27**: **15/24 active** and
**0/3 known limitation**, at
`artifacts/source-discovery-2026-11-13/development-full-v7.json`.

A separate public-context harness builds a disposable database from the checked-in
four-recipe/four-member fixture, copies configuration only, disables Freezy and
background work, verifies the fixture before evaluation, and removes its own
database/container afterward. It preserves a database dump, immutable image ID,
config, metrics and results. The initial full run was also **15/27**, but its
different allergy profile means those numbers are not a controlled improvement
comparison with the development household. Public artifacts are under
`artifacts/source-discovery-2026-11-13/public-full-v1/`.

Focused retrieval experiments remain historical evidence, including failed changes.
One successful four-recipe repeat did not establish stability: its following two
repeats failed. Only the final complete corpus should determine queue completion.
Friday source pancakes remain explicitly runnable as a known limitation; missing
eligible recipes never justify inventing a URL or relaxing allergies.

## Final-run repairs

A complete intermediate run reached 19/27: all three four-date source requests
passed, but some mixed-source requests exhausted the 24-page allowance. Dutch
seven-day requests also retained unverified plain dishes on their other dates,
despite assessed saved alternatives. The selector now considers those alternatives
before accepting an otherwise structurally complete proposal for a household with
canonical allergies. This changes selection, not ingredient inference or the oracle.
[Intermediate report](../../artifacts/source-discovery-2026-11-13/development-final.json).

A subsequent full local run reproduced a real SDK deadline race. A Retry-After
longer than the remaining deadline could reach a timer rounding gap and send an
early retry before the deadline callback fired. The gate now remembers that the
backoff cannot fit and stops after the bounded wait regardless of that timer gap.
The original transport regression passed ten further repeats, with one provider
request and a timeout outcome each time. No retry or operation budget was raised.

## Final local validation and deployed image

The combined local command passed **653 backend tests, zero skipped; 84 frontend
tests; 28 fixture browser tests plus one real API/Chrome workflow; one production
PWA lifecycle test; and nine Linux scraper tests**. Contract generation, fixture
contracts, vocabulary checks and production compilation also passed. The actual
populated deployment was inspected at 360px and 1280px, with successful read-only
collection/quick-plan geometry and page-error checks.

Evidence: `artifacts/local-operations-2026-11-13/local-verified/` and `deployed/`.
The evaluated/deployed image is
`sha256:c6191f99613803db10a8e15365c6242b5389c20578a7534380f401e2aa86dd6f`.
The final live corpus uses this image, the retained v7 facts contract and the same
24-recipe/four-member dataset. Four human-confirmed records still match their
pre-refresh row hashes and class sets exactly.

## Final measured outcome

The complete final run passed **23/27**: **23/24 active** and **0/3 known limitation**.
Every prompt, selected-date setting and assertion remains unchanged. English source
and mixed-source requests each pass 3/3; Dutch source requests pass 2/3. The one
active failure returned three of four required source mains after 27 candidate
inspections. Bounded discovery is not proof that a fourth suitable recipe does not exist.

| Scenario | Final result | Remaining gap |
| --- | --- | --- |
| distinct-vegetarian-source | 3/3 | None in these repeats. |
| multi-source-courses-and-explicit-chicken | 3/3 | None in these repeats. |
| dutch-distinct-source-request | 2/3 | One repeat found only three of four required source mains. |
| local-recipes-stay-linked | 3/3 | None in these repeats. |
| quick-local-recipes | 3/3 | None in these repeats. |
| conflicting-hard-requirements-are-explicit | 3/3 | None in these repeats. |
| mandatory-saved-collection-membership | 3/3 | None in these repeats. |
| mandatory-quick-auto-collection | 3/3 | None in these repeats. |
| dutch-friday-pancakes-from-source | 0/3 | Friday source/weekday requirement passed 3/3; two other dates retain unverified plain dishes in every repeat. |

The Friday case remains failed under its unchanged full-week oracle. Its earlier
spelling/source-slot limitation is no longer the observed failure: all three
repeats contain the requested source main on Friday. The fixture limitation text
was updated to describe that evidence; its prompt, status, selected dates and all
assertions are identical. Do not count that metadata correction as a scenario pass.

The requests total **616.332 seconds**; there are no advisory-budget warnings.
The correlated audit covers **288 provider calls across 27 planning operations**:
27 intent calls, 206 facts calls, 24 query refinements and 31 generation calls.
Every operation has contiguous provider ordinals, at most **32**, and complete
input-version/hash metadata. All calls have terminal semantic outcomes (275 success,
13 validation failure); none remain completed/responseReceived/abandoned.
The largest research session inspected 27 pages; the longest recorded operation
took 62.014 seconds against the unchanged 300-second limit.

- Results: `artifacts/source-discovery-2026-11-13/development-verified.json`
- Correlated metrics/audit: `verified-metrics.json`, `verified-audit.json`
- Captured diagnostics: `development-verified-app.log`

The 15/27 historical baseline used the same recipe/household content and unchanged
oracle, but older stored AI classes and pipeline code. This is a combined measured
improvement, not a controlled attribution to one prompt or retrieval patch.

**Still open:** the one Dutch candidate-discovery failure; the one shrimp-paste
allergen miss in 150 independent facts checks; and complete assessed coverage on
all dates in the known full-week case. The local UI/operations item is complete.
No deferred cooking, move/undo, shopping persistence or routing feature was added.
