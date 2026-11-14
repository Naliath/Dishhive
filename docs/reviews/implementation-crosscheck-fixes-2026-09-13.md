**Implementation crosscheck repairs — 13–14 September 2026**

The ten reproduced defects in the [crosscheck](implementation-crosscheck-2026-09-13.md)
have fixes and permanent regression coverage. This report separates those repairs
from completion of the broader [application review](application-review-2026-09-09.md).
Existing uncommitted work was retained. No household database migration or application
deployment was performed.

| Finding | Repair and evidence |
| --- | --- |
| 1. Human confirmation overwritten at commit | Facts confirmation version is an EF concurrency token. A losing worker supersedes its work. The PostgreSQL interceptor reproduction now preserves the human confirmation. |
| 2. Pre-intent provider failure ignores restrictions | A failed interpretation returns an unable result with an unresolved requirement. The local-only/provider-outage regression returns no freezer plan. |
| 3. Rules fallback loses guests/version | Shared normalization supplies resolved attendance, current facts/time and local candidate versions. PostgreSQL tests cover the entire fallback, batch storage and acceptance path for an explicit guest and unchanged linked recipe. |
| 4. Reviewed facts/reservations can change unnoticed | Durable review snapshots cover recipe contents, facts confirmation, attendees/restrictions and claimed constraints. Acceptance revalidates current data and compares the submitted reservation. Content hashes include complete ingredient lines, instructions and time. Legacy batches without a snapshot require regeneration. |
| 5. Enrichment leases become stranded | Every poll recovers expired leases. Claims/completions compare ownership and revisions. Source mutations stage jobs in the same commit, and a filtered unique index coalesces pending work. PostgreSQL tests cover competing claims, multiple expired leases, confirmation races and translations racing source edits, including translations that change only child rows. |
| 6. Translation chunks exceed gate budget | Chunking and gate admission share an estimate including system text, schema, messages and corrective turns. Chunking reserves output expansion room and accounts for configured context headroom. The large-recipe regression passes with the actual system/schema/retry envelope. |
| 7. Concurrent freezer retry gives false stock failure | Idempotency is checked again under the stock lock. PostgreSQL returns created/alreadyApplied for competing retries without over-reservation. |
| 8. Dessert/wrong-slot rows satisfy dinner requirements | Final validation checks date, meal and course; a dinner main is required for day coverage. Duplicate removal precedes coverage/count checks. The original slot regressions pass. |
| 9. Browser fixtures hide broken renders | Representative typed fixtures replace catch-all empty responses. Missing routes, page exceptions and Angular console errors fail tests. Fixed dates/time zone, content assertions and a 0.5% screenshot tolerance accompany refreshed baselines. |
| 10. Scraper update rollback/lock failures | Lock initialization is covered by cleanup, post-activation failures restore the prior target, and startup reconciles interrupted activation before serving. Tests cover the two reproductions and restart completion/rollback. |

The .NET reproductions now live in [Crosscheck](../../src/Dishhive.Api.Tests/Crosscheck/),
including the unchanged 250-character-title/explicit-guest control. Scraper recovery
tests live in [test_recovery.py](../../src/dishhive-scraper/test_recovery.py).

Related repairs include bounded AI admission queues and operation budgets, abandoned
validation recovery, diagnostic-text expiry, import operation correlation, final
partial/unable telemetry, task-statistics refresh cancellation, and rejection-only
review submissions. The review dialog retains failed selections, preserves unselected
items, offers retained imports for review, and retries only unapplied items.

REST and MCP now use separate credentials; the MCP credential cannot authorize REST
mutations. Expensive endpoints have an application concurrency limit as well as a
rate limit. The optional [remote overlay](../../docker-compose.remote.yml) requires
distinct credentials and a unique database password, keeps loopback bindings, and
disables demo seeding. It still requires an authenticated TLS proxy supplied by the
operator; it is not a built-in account/login system.

**Verification**

| Check | Result |
| --- | --- |
| Full backend suite, disposable PostgreSQL 16 enabled | 487 passed, zero skipped |
| Angular unit suite | 62 passed across 15 files |
| Browser fixture typecheck | Passed |
| Browser journeys, without updating baselines | 17 passed |
| Screenshots | All 14 reviewed, including populated metrics at 360px and desktop dark mode |
| Angular production build | Passed; initial bundle 657.91 kB, estimated transfer 153.62 kB |
| OpenAPI/NSwag | Regenerated; repeated generation produces the same client |
| Scraper tests against checked-out source in an isolated Linux container | 9 passed |
| Remote Compose configuration validation | Passed with placeholder credentials; not deployed |
| Evaluator transport-failure regression | Both failed requests retained; execution continued |
| Whitespace check | Passed |

Browser journeys cover review generation, partial acceptance, transport retry,
retained imports/needsReview, rejection-only completion, and an edit whose first save
fails. PostgreSQL checks and Linux scraper tests run locally, and screenshots use
the reviewed Windows browser platform. Runtime exceptions are checked independently
of images. Verification is local-only, as specified on 12 November 2026.

Two migrations were added: `CloseCrosscheckBoundaries` stores review snapshots and
facts confirmation versions; `CoalescePendingEnrichment` supersedes duplicate pending
rows before adding its unique index. Both ran against disposable PostgreSQL databases.

The final source review reproduced one additional translation race: unchanged recipe
headers let EF save translated ingredient/step rows without issuing a guarded recipe
update. The new PostgreSQL test failed with the worker overwriting the edited ingredient,
then passed after translation was made to always issue that conditional parent update.
The complete backend suite above includes this regression and the empty-ingredient
failure-state regression.

**Live planning evaluation**

Live requests use the updated API at `localhost:5110` with a disposable database,
20 embedded demo recipes and four demo members. Existing local search/scraper/Freezy
services supply reads; suggestions are never accepted. The configured provider is
`openai-compatible`, model `deepseek/deepseek-v4-flash` through OpenRouter. This is a
different database from the audit's deployed-system run, so the results are not a
controlled before/after performance comparison.

The final run uses intent contract `planning-intent-v3.1`, temperature 0.3,
20,000 input / 12,000 output tokens for planning, a 60-second per-call timeout,
a 300-second operation deadline, one configured corrective retry, one concurrent
model call, 32 queued calls and a 65,536-token context budget. Diagnostic text was
enabled only for the disposable evaluation API. Correlated metrics and logs are
retained with the report; summed recipe-resolution time can exceed wall time because
resolutions run concurrently.

The first complete repetition produced 2 passed / 16 failed. All source plans were
partial/unable; checked candidates frequently contained Milk, which conflicts with a
demo attendee's allergy. Local-only plans retained their links but the corpus rejected
unknown-facts warnings. Quick-local results were rejected when their cooking time
could not be verified within 30 minutes. One contradictory request was incorrectly
interpreted as satisfiable; two correctly returned unable. The interpretation prompt
was then revised to preserve both contradictory clauses, with a new contract version
and capability fingerprint.

The first evaluation attempt also exposed a runner defect: a client timeout aborted
the corpus without saving earlier results. The runner now checkpoints each iteration,
records transport failures and continues, with a client timeout above the default
application deadline. The subsequent run was interrupted after five requests when
the previous session ended; its checkpoint is preserved as
`live-planning-interrupted-after-5.json`. The final three-iteration run starts again
on 14 September with a fresh disposable database and is recorded separately.

The final result is **3 passed / 15 failed across 18 requests**. The existing
known-limitation pancake scenario remains skipped; its expectations were not weakened.

| Scenario | Passed | Request duration |
| --- | --- | --- |
| Distinct vegetarian source recipes | 0/3 | 25.0–66.5 seconds |
| Multiple sources, courses and explicit chicken | 0/3 | 103.4–155.7 seconds |
| Dutch distinct-source request | 0/3 | 37.7–42.9 seconds |
| Local recipes retain links | 0/3 | 8.8–13.3 seconds |
| Quick local recipes | 0/3 | 9.6–16.5 seconds |
| Contradictory hard requirements | 3/3 | 1.3–7.6 seconds |

All contradictory requests returned unable. These three passes establish the observed
result, not a guarantee of stable natural-language interpretation. Source requests
remained partial/unable: candidates conflicted with allergies or generation/repair
failed to populate and claim the required slots. Mixed-source intent captured the
requested counts, weekdays and courses, but subsequent outputs included invalid claims,
wrong courses or repeated recipes, and two corrective responses were unparseable.
Local-only requests kept their recipe links; the corpus rejected their unverified-facts
warnings. Quick-local intent retained the 30-minute limit in all three runs. Returned
recipes met that time ceiling, but there were too few valid distinct selections and
their dietary facts still required review.

The requests totalled 721.6 seconds, with four advisory performance overruns and no
transport failures. Planning telemetry recorded ten partial, five unable and three
success outcomes; the three complete local plans still failed the stricter corpus
warning assertion. Task telemetry recorded 109 calls: 101 successful, eight failed
validation/parsing, zero cancelled and zero incomplete. The mixed-source runs spent
69–91 seconds in planning completion calls, 6–9 seconds in search, and 57–215 summed
seconds resolving recipes (including facts assessment and overlapping waits).

Detailed evidence is under the ignored [artifacts/crosscheck-fixes](../../artifacts/crosscheck-fixes/)
directory. `live-planning-final-3x.json` contains every result;
`live-planning-final-metrics.json`, `live-planning-final-operation-metrics.json` and
`live-app.log` retain configuration, normalized intent and correlated execution evidence.
Keep those artifacts separately when sharing this report. The live run exercised the
final planning implementation; the last translation commit-guard change was validated
by the PostgreSQL suite and does not participate in suggestion generation.
The temporary API was stopped and its disposable PostgreSQL container removed after
collecting the evidence. The existing application stack was not rebuilt or deployed.

**Broader plan still requiring work**

These items from the original completion map are not established as complete by the
ten repaired reproductions:

| Area | Remaining work |
| --- | --- |
| R3 | Typed identity/membership for mandatory global collections, specific corrective interpretation errors, and deterministic repair that selects against every final constraint. Natural-language interpretation remains fallible. |
| R4 | Complete attendance endpoint matrix beyond the added fallback/acceptance coverage. |
| R6 | Reconciliation when external stock changes independently of Dishhive reservations. |
| R7 | A shared validated-execution helper and semantic eligibility distinct from response-format capability. Disposal and recovery close abandonment, but semantic capability failures remain warnings. |
| R8 | Explicit provider 429/backoff behavior and accounting for hidden SDK transport retries in total provider attempts. |
| R9 | Complete correction/input-revision telemetry. |
| R10 | Production service-worker journey and live facts/translation/extraction, injection, and search-outage corpora. Live planning quality is not a green gate. |
| R11 | Broader Material-control evaluation for quick-plan and collection chips. Populated metrics, long content and review failure states now have browser evidence. |
| R12 | Generated client-class adoption, remaining handwritten wire contracts, recipe mutation service extraction and consistent concurrency/error responses. |
| R13 | An integrated authenticated remote/shared profile beyond operator-supplied proxy authentication. Credential separation and application concurrency are implemented. |
| Additional operations | Household clock/time zone, complete vocabulary localization, production PWA freshness verification and backup/restore rehearsal. |

Attendance UX, shopping workflow, meal movement and cooking mode remain the explicitly
deferred feature row from the original review.
