**Completed job: R7/R8 execution semantics and provider attempts — 12 November 2026**

The next execution/budget job is complete and deployed at **http://localhost:5100**.
Planning eligibility now requires semantic evidence, every feature uses shared validated
execution, and the gate owns observable transport retries within one operation budget.
Normal development settings are restored: Freezy is reachable, diagnostic-text retention
is off, and the existing 24-recipe development database is retained.

Final image: `sha256:55ad4188ba6c1a11f5dca2addd6dcf3cd83039a572435746bbcbe8dd9f047428`.
The [current checklist](application-review-status-2026-11-12.md) marks this job complete
and moves persistence/contract cleanup to the next item. Reliable source discovery and
facts accuracy remain open product outcomes.

**Execution and eligibility**

- [The gate](../../src/Dishhive.Api/Services/Ai/AiExecutionGate.cs) checks configuration
  itself. Both production intent and generation require passing planning quality.
  A working response format alone, or missing/failed semantic evidence, cannot authorize
  planning. A deliberately marked intent probe can run before that verdict; a normal
  generation request cannot use the probe exemption.
- [Capability results](../../src/Dishhive.Api/Services/Suggestions/AiModelTester.cs)
  distinguish format compatibility from `PlanningEligible`: format plus the planning
  example plus both multilingual intent examples must pass. Model-list availability
  remains advisory. Failed quality uses the failure cooldown, including across restart.
  The capability fingerprint is now `capability-v3`, invalidating older evidence.
- [Shared validated execution](../../src/Dishhive.Api/Services/Ai/AiValidatedExecution.cs)
  owns response lifetime and exactly one terminal semantic outcome. Facts, translation,
  extraction, intent, generation and capability probes all use it. Validator exceptions,
  caller cancellation and deadline expiry receive terminal outcomes. Existing disposal
  and maintenance recovery remain available for interrupted work.
- Settings describe the checks as finite planning examples. Other AI tasks remain
  available without planning eligibility, with their quality explicitly unverified;
  their returned results still undergo task-specific contract/domain validation.
  Passing these probes is not a guarantee of a correct complete live plan.

**Whole-operation attempts and retries**

[Operation scopes](../../src/Dishhive.Api/Services/Ai/AiOperationBudget.cs) provide a
deadline, correlation id and thread-safe provider-attempt counter. Nested extraction and
facts calls during research inherit the planning scope, including parallel work. Chunks
and corrections do not get fresh allowances. Intent corrections now use the task profile
rather than a fixed two-attempt loop. Individual task deadlines remain bounded by the
enclosing operation deadline.

Capability testing is a separate shared operation with its own limits and host-lifetime
cancellation. A planning caller's wait still observes its deadline; abandoning one caller
does not cancel a probe needed by others or charge it to another caller's attempt budget.

[SDK retries are disabled](../../src/Dishhive.Api/Services/Suggestions/ChatClientFactory.cs).
The gate owns retries for transient network failures and HTTP 408/429/500/502/503/504.
It respects `Retry-After`, releases admission during backoff, and cannot reset the
deadline or exceed the shared attempt allowance. Each admitted provider-client invocation
has its own durable task row. This counts completion attempts; a network failure does
not establish whether the remote server received or processed a request.

The recorded live configuration was:

| Setting | Value / meaning |
| --- | --- |
| `AI_OPERATION_TIMEOUT_SECONDS` | **300**, shared AI operation deadline. |
| `AI_MAX_OPERATION_ATTEMPTS` | **32**, shared admitted provider calls, including nested work and transport retries. |
| `AI_MAX_TRANSPORT_RETRIES` | **1**, gate-owned retries; configurable from zero to three. |
| `AI_MAX_RETRIES` | **1**, task-level semantic corrections; independent of transport retries. |
| Standard / extended task timeout | **60 / 300 seconds**, also limited by the enclosing operation. |

Defaults are aligned in options, appsettings, Compose and `.env.example`.
[Selected non-secret configuration](../../artifacts/execution-semantics-2026-11-12/live-config.json).

**Metrics and migration**

The nullable `AiTaskRun.OperationAttempt` records the operation-wide ordinal. Positive
values identify admitted provider calls; **0** means no provider call was admitted;
**null** preserves unknown historical counts. Existing `Attempt` remains the feature's
completion/correction index. The API and statistics page expose both; legacy data displays
“not recorded” and rejected work displays “not sent”. Rate-limited and exhausted-budget
outcomes have distinct translated labels.

The [migration](../../src/Dishhive.Api/Data/Migrations/20261112215618_RecordAiOperationAttempts.cs)
adds one nullable column and preserves existing rows. Its PostgreSQL regression migrates
an existing task row through downgrade/upgrade, verifies the historical ordinal remains
unknown, and verifies a new ordinal persists. OpenAPI and the generated client were
regenerated reproducibly.

**Verification**

Two initial regressions failed before the fix: format-only evidence authorized planning,
and repeated calls bypassed a declared operation limit.
[Before results](../../artifacts/execution-semantics-2026-11-12/before.trx).

The new [execution regressions](../../src/Dishhive.Api.Tests/Services/AiExecutionSemanticsTests.cs)
include actual OpenAI-compatible SDK calls through a controlled HTTP handler. They count
requests for 429/503 retries, non-retryable errors, disabled retries and exhausted budgets;
verify long `Retry-After` waits cannot send early or outlive the deadline; distinguish
caller cancellation; prove backoff releases admission; check parallel child scopes and
independent probes; and verify validator exceptions/deadlines terminate correctly. These
transport failures are simulated through the real SDK, not claimed as live outages.

| Local check | Final result |
| --- | --- |
| Complete backend, including PostgreSQL and the real API browser workflow | **562 passed, 0 failed, 0 skipped**, 16 seconds; [TRX](../../artifacts/execution-semantics-2026-11-12/local-final/backend.trx). |
| Angular unit tests | **64 passed across 16 files**; [log](../../artifacts/execution-semantics-2026-11-12/local-final/frontend-tests.log). |
| Browser fixture contracts | **Passed**; [log](../../artifacts/execution-semantics-2026-11-12/local-final/browser-contracts.log). |
| Fixture browser suite | **20 passed**; two new cases distinguish format support from failed/passed planning quality. [Log](../../artifacts/execution-semantics-2026-11-12/local-final/browser-fixtures.log). |
| Real API/PostgreSQL Chrome journey | **Passed**, included in the backend total; [log](../../artifacts/execution-semantics-2026-11-12/local-final/api-browser.log). |
| Linux scraper suite | **9 passed**, source mounted read-only and network disabled; [log](../../artifacts/execution-semantics-2026-11-12/local-final/linux-scraper.log). |
| Production frontend / image build | **Passed**; [frontend](../../artifacts/execution-semantics-2026-11-12/local-final/production-build.log), [image](../../artifacts/execution-semantics-2026-11-12/docker-final-build.log). |
| Generated contracts | **Byte-identical on regeneration**; [log](../../artifacts/execution-semantics-2026-11-12/local-final/api-generation.log). |
| Complete local command | **Passed**; [summary](../../artifacts/execution-semantics-2026-11-12/local-final/summary.json). |
| Capability probe | **All eight checks passed**, JsonSchema, planning eligible, 10.104 seconds; [result](../../artifacts/execution-semantics-2026-11-12/capability.json). |
| Final-image configured planning | **3/3 local-planning repeats passed**; [report](../../artifacts/execution-semantics-2026-11-12/final-image-local-3x.json). |
| Restored development deployment | **Healthy**, 24 recipes, Freezy reachable, retention disabled; [health](../../artifacts/execution-semantics-2026-11-12/restored-health.json), [settings](../../artifacts/execution-semantics-2026-11-12/restored-deployment.json). |

The local command is `./scripts/verify-local.ps1`. The current working tree, including
the pre-existing staged implementation, is retained; this job does not create a commit.

**Live corpus and attempt audit**

The complete corpus ran three times per scenario against the same synthetic positive-control
dataset as the previous job: 20 AI-assessed public demo recipes, four explicitly confirmed
synthetic recipes and four fictional members. Freezy was disabled during evaluation.
No assertions or household restrictions were weakened. The final executor guard against
unconfigured AI was added afterward; it does not alter the configured path, which was
then repeated three times on the final image.

Results remain **15 passed / 12 failed across 27 requests**: **15/24 active**, plus **0/3**
for the known limitation. Local linking, quick meals, manual/automatic collection membership
and contradictions each pass 3/3. Distinct source counts, multiple-source desserts and
Friday pancakes remain incomplete. The 20 original recipes' classifications have not been
independently verified; positive controls establish feasibility, not classifier accuracy.
[Report](../../artifacts/execution-semantics-2026-11-12/live-3x.json),
[scenario summary](../../artifacts/execution-semantics-2026-11-12/scenario-summary.json).

All **117** correlated provider attempts across **27** operations have contiguous,
unique ordinals starting at one. The largest operation used **11/32** attempts. Outcomes
were **105 success and 12 validation failures**, with zero remaining response-received,
completed or abandoned rows. No transport failures occurred in the live corpus; retry
failure behavior is established by the controlled SDK regressions.
[Audit](../../artifacts/execution-semantics-2026-11-12/attempt-audit.json),
[all calls](../../artifacts/execution-semantics-2026-11-12/live-calls.json),
[summary](../../artifacts/execution-semantics-2026-11-12/live-summary.json).

Summed request time was **369.848 seconds**. Stored operations averaged **13.664 seconds**,
P95 **42.319 seconds**; completion time averaged **10.723 seconds**. The run recorded
15 searches, three empty searches, 72 resolutions, 12 resolution failures and zero parse
failures. This is effectively the previous run's duration; no performance improvement is
inferred from the sample. [Metrics](../../artifacts/execution-semantics-2026-11-12/live-metrics.json),
[logs](../../artifacts/execution-semantics-2026-11-12/live-app.log).

**Rendered review evidence**

Existing Material layouts/classes are reused; no SCSS, global density or theme changes
were introduced. One mobile statistics baseline intentionally changed to show both
attempt indices. The other baselines remain intact. The capability panel's failed/passed
states were inspected at 360px and 1280px, as were the real deployed settings panel and
populated task card. No page exceptions or measured horizontal overflow occurred.

- Statistics: [before 360px](../../artifacts/execution-semantics-2026-11-12/ai-statistics-call-before-360.png),
  [after 360px](../../artifacts/execution-semantics-2026-11-12/ai-statistics-call-after-360.png).
- Format passes but planning is blocked: [360px](../../artifacts/execution-semantics-2026-11-12/capability-false-360.png),
  [1280px](../../artifacts/execution-semantics-2026-11-12/capability-false-1280.png).
- Actual deployed checks: [360px](../../artifacts/execution-semantics-2026-11-12/deployed-settings-360.png),
  [1280px](../../artifacts/execution-semantics-2026-11-12/deployed-settings-1280.png).
- Actual provider ordinal versus completion index: [360px](../../artifacts/execution-semantics-2026-11-12/deployed-statistics-360.png),
  [1280px](../../artifacts/execution-semantics-2026-11-12/deployed-statistics-1280.png),
  [geometry/results](../../artifacts/execution-semantics-2026-11-12/deployed-ui.json).

Token admission remains a conservative estimate. Capability examples do not establish
general model accuracy, and the full live planning gate remains red. Those limits and
the remaining persistence, contract and product work remain visible in the checklist.
Raw artifacts are ignored by Git; retain them separately when sharing the evidence.
