**Completed job: local attendance and workflow coverage — 12 November 2026**

The queued R4/R10 endpoint/workflow job is complete. Development verification is
local-only. The hosted workflow was removed, including its previously staged entry;
its requirements were removed from project guidance, review records and plans.
Application behavior did not need a production change: the shared attendance policy
passed the new endpoint matrix. The development app remains at **http://localhost:5100**
with its existing 24 recipes and database retained.

Run the complete verification from the repository root:

```powershell
./scripts/verify-local.ps1
```

The command starts the local Compose database service, compares generated API contracts
before and after regeneration, runs frontend tests/build and browser contract checks,
runs the entire backend suite with disposable PostgreSQL databases and the real API
browser journey, runs the existing browser fixture suite, builds the scraper and runs
its current source in a network-disabled Linux container. It writes logs, TRX results
and browser artifacts to `artifacts/local-verification`; `-ArtifactDirectory` selects
another output directory. Prerequisites and database overrides are in the
[README](../../README.md#running-tests).

**Coverage added**

The [HTTP attendance matrix](../../src/Dishhive.Api.Tests/Integration/AttendanceEndpointTests.cs)
has 28 combinations: seven selections across manual create, update, generation and
acceptance, plus one regression for attendance changed after generation. Selections are
omitted, explicitly empty, guest-only, household plus guest, inactive, unknown and duplicate
guest ids. Assertions verify returned attendance and persisted attendee links. Invalid
updates preserve the original meal and attendees; invalid generation never calls the
provider; invalid acceptance creates no meal. Changing reviewed attendance requires
another review. These matrix tests use the application's real HTTP pipeline with the
existing isolated EF InMemory test provider.

Quick plan uses the same create endpoint and currently chooses household defaults.
The real browser journey verifies its submitted and persisted household ids while an
active guest and an inactive household member also exist. It does not add a guest picker
or per-day attendance composition to that dialog.

Three [PostgreSQL HTTP workflows](../../src/Dishhive.Api.Tests/Integration/LocalWorkflowTests.cs)
run against fresh, migrated, randomly named databases:

- Import through the production source provider using stored HTML, edit the recipe,
  explicitly confirm facts, generate for household plus guest and accept the result.
  Assert recipe identity/title, guest persistence, replacement of the vague idea,
  shopping quantities, review outcome and idempotent retry. The quantity control is
  **100 g / four recipe servings × two selected people = 50 g**.
- Edit a saved recipe after review, verify `needsReview` and no saved meal, then generate
  a new review and accept the current version.
- Deactivate a guest after generation and verify acceptance fails without substituting
  household members or creating a meal.

The [Chrome workflow](../../src/dishhive-web/e2e/api-workflow.spec.ts) serves the production
frontend from an isolated loopback Kestrel host backed by migrated PostgreSQL. It imports
through the UI, edits and saves through the UI, uses quick plan, generates one dinner,
reviews and accepts it, retries acceptance and reads persisted meals/shopping data.
No browser API responses are intercepted. Runtime exceptions and failed API responses
fail the test. Family setup and explicit facts confirmation use the same real API.

The [test factory](../../src/Dishhive.Api.Tests/LocalWorkflowFactory.cs) controls only
external boundaries: recipe-page/image fetches use stored fixtures, the suggestion
provider returns deterministic proposals and Freezy returns an empty list. Production
post-processing normalizes the proposals. Testing disables background model jobs.
Consequently these tests need no model key, external recipe site or live search and
make no claim about model quality. Each PostgreSQL database is deleted after its test;
the development database and Freezy data are not reset.

**Final local evidence**

The full command exited successfully. [Machine-readable summary](../../artifacts/local-workflow-2026-11-12/full/summary.json).

| Check | Result / evidence |
| --- | --- |
| Backend including PostgreSQL and the real API browser workflow | **538 passed, 0 failed, 0 skipped**, 14 seconds; [TRX](../../artifacts/local-workflow-2026-11-12/full/backend.trx), [log](../../artifacts/local-workflow-2026-11-12/full/backend.log). |
| Frontend unit tests | **64 passed across 16 files**; [log](../../artifacts/local-workflow-2026-11-12/full/frontend-tests.log). |
| Production frontend build | **Passed**; [log](../../artifacts/local-workflow-2026-11-12/full/production-build.log). |
| Generated API contracts | **Regeneration byte-identical** to the current working files; [log](../../artifacts/local-workflow-2026-11-12/full/api-generation.log). The check works with the existing uncommitted source. |
| Browser TypeScript contracts | **Passed**, including the new workflow test; [log](../../artifacts/local-workflow-2026-11-12/full/browser-contracts.log). |
| Existing fixture browser tests | **18 passed**, 22.6 seconds, baselines retained; [log](../../artifacts/local-workflow-2026-11-12/full/browser-fixtures.log). |
| Real API Chrome workflow | **1 passed**, included in the backend total; [log](../../artifacts/local-workflow-2026-11-12/full/api-browser.log). The [final screenshot](../../artifacts/local-workflow-2026-11-12/full/api-browser/api-workflow-import-edit-q-7f69d--through-the-real-local-API/workflow-complete.png) was inspected at 1280px. |
| Linux scraper extraction/update/recovery | **9 passed**, current source mounted read-only, network disabled; [log](../../artifacts/local-workflow-2026-11-12/full/linux-scraper.log). No live package update or running service restart was performed. |
| Development app | **Healthy**, existing 24-recipe dataset retained; [probe](../../artifacts/local-workflow-2026-11-12/development-health.json). |
| Test database cleanup | **Zero workflow databases remain** after verification; [probe](../../artifacts/local-workflow-2026-11-12/database-cleanup.json). |

During runner verification, TypeScript caught an untyped Node environment reference in
the screenshot path; the test now uses Playwright's output-path API. Docker Compose also
returned an obsolete running-container image id after rebuilding. The runner now resolves
the freshly built tag from build metadata, and handles ordinary Windows native stderr
progress separately from the exit code. The final complete run passes both checks.

**Remaining scope**

The [current checklist](application-review-status-2026-11-12.md) marks this queued job
complete. The subsequently completed [R7/R8 execution job](execution-semantics-fix-2026-11-12.md)
adds shared validation, quality eligibility and provider-attempt accounting; persistence
and contract cleanup is now the next item.
R10 as a broader review package remains partial: live planning/source/facts quality,
additional model-task and outage corpora, production cache freshness and broader
family/shopping workflows remain visible work. This test-only job did not rerun or
change the previous live planning results or assertions. Per-day attendance remains
a deferred product feature.

Raw artifacts are ignored by Git. The test sources, local runner and this report are
the durable record; retain the linked artifacts separately when sharing evidence.
