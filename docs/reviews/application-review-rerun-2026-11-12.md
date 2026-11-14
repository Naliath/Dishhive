**Fresh-database deployment and test rerun — 12 November 2026**

The rebuilt development application is running at **http://localhost:5100**. The local Dishhive database was cleared with the user's explicit authorization, recreated, migrated and seeded with the built-in **20 public recipes, four fictional demo members and 14 historical meals**. The full deterministic suites pass. The live planning quality gate is still red, and a real statistics-page defect was reproduced.

This supplements the [current completion checklist](application-review-status-2026-11-12.md). It does not claim completion of the original broader plan.

**Deployment and scope**

- Verified that the active database mounted `dishhive_postgres_data`, labelled for the Dishhive Compose project and `postgres_data` service volume. Removed that volume after taking down the Dishhive stack; recreated it empty. Other database volumes were not deleted.
- Rebuilt `app` and `scraper` from the current working tree at `5cbf7a8`, including its existing uncommitted/untracked implementation. App image: `sha256:fb9a69c6dd1dadaaac3713fb74dd7c2a70bd38052cf8cca134bc92680c1c2951`; scraper image: `sha256:9f0db8d94fe22319b47ab2814f2e8b705097d0b75d56744a98b6a942d1308a65`.
- The backend suite uses the new PostgreSQL 16 server; relational test classes create and remove their own databases. Application migrations also ran against the fresh development database.
- Live generation used only verified built-in demo data and canned corpus prompts. The real Freezy feed was temporarily disabled for that evaluation. It was restored afterward, together with the standard Compose diagnostic settings. The existing Freezy containers were started without clearing their data.
- The final integration probe reports AI reachable/capability passed, Freezy reachable, scraper 15.11.0 reachable, and SearXNG operational. The rebuilt app, database and scraper are healthy. No remote/TLS deployment was run.

**Verification results**

| Check | Result |
| --- | --- |
| Full backend, including PostgreSQL races/migrations | **487 passed, 0 failed, 0 skipped**, about 11 seconds test execution. [Final TRX](../../artifacts/review-status-2026-11-12/rerun/backend-postgres-final.trx). |
| Angular unit suite | **62 passed across 15 files**. |
| Browser fixture typecheck | **Passed**. |
| Existing Playwright suite, baselines unchanged | **17 passed**, about 26.4 seconds. These journeys use mocked API responses. |
| Docker production build | **Passed** for API/Angular app and scraper. Angular initial bundle 657.91 kB / 153.62 kB estimated transfer. |
| Linux scraper extraction/update/recovery suite | **9 passed**, current source mounted read-only into the rebuilt image, network disabled. |
| Deployed production browser smoke | **Eight real routes passed** at 360×900: planner, recipes, recipe detail, family, statistics, shopping, settings and AI statistics. No page exceptions or page-level horizontal overflow. No API response mocking. This is basic rendering evidence; the separate metrics-content probe found the defect below. |
| Production service worker | **A populated cached shopping list with 107 items reloaded offline with identical content.** Real production worker and API, no mocks. API-outage freshness, mutation/image invalidation, application updates and multi-device behavior remain unverified. |
| Capability test | **All eight checks passed**, JsonSchema response mode; initial fresh run about 10.45 seconds. This did not predict corpus quality. |
| Live active corpus, three iterations | **3 passed / 15 failed**, 18 requests. |
| Known-limitation pancakes, explicitly included | **0 passed / 3 failed**. |
| Complete live run | **3 passed / 18 failed**, 21 requests; 283.823 seconds summed request duration, zero transport failures, zero advisory performance warnings. |

Nine deployed screenshots were inspected. Read-only production smoke evidence is in [production-smoke.json](../../artifacts/review-status-2026-11-12/rerun/production-smoke.json), with `deployed-*-360.png` captures alongside it. As with the existing screenshot suite, a capture of the app's outer viewport is not proof that every row inside a scrolling surface was inspected.

**A stale test date was repaired without changing the assertions**

The first relational run produced **486 passed / 1 failed / 0 skipped**. `CompetingBatches_CannotReserveTheFinalFreezerUnitTwice` used **5 October 2026**, now in the past. The application intentionally excludes past meals from current freezer reservations, so both accepts returned `created`; the test no longer exercised competing future reservations.

Changed the reservation input and the shared crosscheck day to `DateOnly.FromDateTime(DateTime.Today).AddDays(7)`. The expected outcomes, stock-count assertion and production implementation were unchanged. The final complete suite passed with future reservations. [Initial failing evidence](../../artifacts/review-status-2026-11-12/rerun/backend-postgres.trx), [reservation test](../../src/Dishhive.Api.Tests/Integration/PostgreSqlAcceptanceTests.cs#L75), [crosscheck fixture](../../src/Dishhive.Api.Tests/Crosscheck/PersistenceCrossTests.cs#L24).

These two test-data adjustments and the review documentation are the only source changes made for this rerun. No application fix or corpus expectation was changed to obtain a green result.

**New confirmed defect: AI statistics hides its calls (R9/R10)**

On the real deployed page, the browser requests:

```text
/api/integrations/ai/planning-runs?count=30&task=undefined
```

The controller treats the literal string `undefined` as a task filter. In the captured reproduction the page showed **0 retained calls**, while the unfiltered endpoint returned **83 calls**. The completed run eventually retained 88. Persistence is working; the initial “all tasks” request hides its data.

`AiPlanningStatsPage.refresh()` passes `{ task: this.selectedTask() ?? undefined }`, and `IntegrationsService.getAiPlanningMetrics()` spreads the object directly into Angular query parameters. The browser probe confirms the actual wire URL, rather than only inferring it from the source. Existing page tests stub the service; the browser fixture matches the metrics path without enforcing query semantics, so both suites miss this defect.

Required next change: omit absent query values before calling `HttpClient`, then add an HTTP-level query test and a real all-tasks → task → all-tasks browser assertion. Verify that counts and task controls remain populated. **This defect was recorded, not fixed in this test-only followup; the subsequent [metrics-query implementation](metrics-query-fix-2026-11-12.md) now closes it.** Evidence: [browser probe](../../artifacts/review-status-2026-11-12/rerun/metrics-browser-probe.json), [direct comparison](../../artifacts/review-status-2026-11-12/rerun/metrics-undefined-filter.json), [page](../../src/dishhive-web/src/app/pages/ai-planning-stats/ai-planning-stats.page.ts#L38), [service](../../src/dishhive-web/src/app/services/integrations.service.ts#L34).

**Live evaluation and interpretation**

Configuration: `openai-compatible`, `deepseek/deepseek-v4-flash` via OpenRouter; `planning-intent-v3.1`; temperature 0.3; thinking disabled; 20,000 input / 12,000 output tokens; 20,000 variable-prompt budget; 60-second ordinary timeout / 300-second source-planning deadline; one configured corrective retry; one concurrent model request; queue limit 32; context estimate 65,536 tokens. Diagnostic text was enabled only for evaluation capture. Freezy was disabled, so this is **not a controlled before/after benchmark** against September's run with external inventory.

| Scenario | Passed | Durations | Observed result |
| --- | --- | --- | --- |
| Distinct vegetarian source | 0/3 | 14.4–22.5 s | All unable. Research resolved six candidates in each run, but selected recipes contained Milk, conflicting with the demo attendee's restriction; final filtering removed them. |
| Multiple sources/courses/chicken | 0/3 | 26.2–33.9 s | All partial. Source counts, desserts and the verified chicken slot remain unfilled. Some normalized source constraints had null search queries despite the prompt requiring one; candidate resolution varied from zero to eight. Resolved desserts and vegetarian candidates also conflicted with Milk. |
| Dutch distinct source | 0/3 | 14.0–19.3 s | All partial; no required source coverage in the returned plans. Fallback dishes carry unverified-facts warnings. |
| Saved local links | 0/3 under full oracle | 5.5–8.6 s | All complete and linked; the failures are unverified-facts allergy warnings. The original identity/link loss remains fixed. |
| Quick local recipes | 0/3 | 4.0–4.4 s | All partial, one linked 30-minute recipe instead of two; facts still require review. The time ceiling was retained. |
| Contradictory requirements | 3/3 | 1.4–1.6 s | All correctly unable with unmet requirements. |
| Dutch pancakes, known limitation | 0/3 | 12.8–21.4 s | All partial; requested Friday source main absent, with unverified fallback warnings. Still a limitation, now measured rather than skipped. |

The fresh seed does not enqueue assessments for its 20 local recipes. They remain unassessed during this baseline; their warnings are not proof of a known allergen conflict. This differs from external candidates, whose resolved facts were assessed during research. Do not interpret all 18 failures as unsafe returned meals or all as a model outage. The important open outcomes remain adequate constraint-aware candidate selection/research and a ready, assessed local recipe set.

Planning telemetry records **12 partial, six unable and three success operations**. The three success operations are the complete local-link plans; the contradiction passes correctly record unable. Task telemetry contains **88 calls including startup capability work: 76 success, 12 validation failures, no cancelled or incomplete calls**. Forty external-recipe facts calls produced validated outputs, which establishes contract acceptance, not independently measured classification accuracy.

Complete evidence: [live results](../../artifacts/review-status-2026-11-12/rerun/live-planning-3x.json), [summary](../../artifacts/review-status-2026-11-12/rerun/summary.json), [task/planning metrics](../../artifacts/review-status-2026-11-12/rerun/live-metrics.json), [per-operation metrics](../../artifacts/review-status-2026-11-12/rerun/live-operation-metrics.json), [correlated app log](../../artifacts/review-status-2026-11-12/rerun/live-app.log), [configuration](../../artifacts/review-status-2026-11-12/rerun/deployment.json), [final integrations](../../artifacts/review-status-2026-11-12/rerun/integrations-final.json). Raw artifacts are ignored by Git; retain them separately when sharing this report.

**Effect on the completion decision**

R2's persistence races and R14's Linux recovery tests now have fresh passing evidence. The current development deployment and basic production offline read are verified. R3/live planning remains incomplete; R9 now has a newly reproduced user-visible defect. Full cache freshness, broader workflow coverage, remote deployment, backup/restore and the previously deferred product features remain outstanding.
