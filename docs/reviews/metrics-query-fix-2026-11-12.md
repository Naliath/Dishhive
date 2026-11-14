**Completed: AI statistics query fix — 12 November 2026**

The first job in the [current review checklist](application-review-status-2026-11-12.md) is complete and deployed at **http://localhost:5100/settings/ai-stats**. The page now shows **88 recorded model calls**, filters to **40 recipe-facts calls**, and restores all 88 when the filter is cleared or the page is refreshed.

Previously, the page passed `{ task: undefined }` to the service, which sent the literal query `task=undefined`. The server correctly treated that string as a task filter and returned no matching calls. The service now omits unset optional filter values while preserving supplied filters and the requested count. [Implementation](../../src/dishhive-web/src/app/services/integrations.service.ts#L34).

**Regression evidence**

| Check | Before fix | After fix |
| --- | --- | --- |
| HTTP query regression | Failed: all optional parameters appeared as `undefined` in the URL | Passed: an all-tasks request sends only `count=30`; explicit filters/count retain their values |
| Query-sensitive browser regression | Failed: initial request included the task parameter, and fixture rejected task `undefined` | Passed: all tasks → recipe translation → all tasks produces counts 12 → 4 → 12 and queries with absent → selected → absent task |
| Full Angular suite | Existing tests did not catch the wire-level defect | **64 passed across 16 files** |
| Browser fixture typecheck | — | **Passed** |
| Full browser suite | — | **18 passed**, existing screenshot baselines unchanged |
| Production build | — | **Passed**, initial bundle 657.91 kB / 153.59 kB estimated transfer |
| Real deployed mobile and desktop | Both showed 0 calls despite 88 in the unfiltered API | At **360px/light and 1280px/dark**, both show **88 → 40 → 88**, then 88 after Refresh; four task controls populate; no page exceptions or page-level horizontal overflow |

The [HTTP tests](../../src/dishhive-web/src/app/services/integrations.service.spec.ts) use Angular's actual request construction rather than a mocked service. The [browser fixtures and journey](../../src/dishhive-web/e2e/ui-regression.spec.ts) now enforce task-query semantics, so a malformed request can no longer receive the unfiltered happy-path fixture. The two focused regressions were observed failing before the production fix; the complete suites passed afterward.

**Deployment and review**

Rebuilt and replaced only the development app container. The existing demo database and its recorded evaluations were retained. Deployed app image: `sha256:062dfb27d5232f1c3dc59a01ce46faac1bcf5594dd3ddd0623ee860e1352795e`.

All six before/after captures were inspected. No template, SCSS, typography, control hierarchy or API contract changed; the existing Material presentation now receives the intended data. The real deployed verification used no API mocks and sent no model-generation requests.

| Surface | Before | After |
| --- | --- | --- |
| Mobile 360px, light | [Empty metrics](../../artifacts/metrics-query-fix-2026-11-12/before-360.png) | [Correct totals](../../artifacts/metrics-query-fix-2026-11-12/after-360.png), [populated task controls](../../artifacts/metrics-query-fix-2026-11-12/after-tasks-360.png) |
| Desktop 1280px, dark | [Empty metrics](../../artifacts/metrics-query-fix-2026-11-12/before-1280.png) | [Correct totals and calls](../../artifacts/metrics-query-fix-2026-11-12/after-1280.png) |

Raw evidence is under the ignored [artifacts/metrics-query-fix-2026-11-12](../../artifacts/metrics-query-fix-2026-11-12/) directory: `unit-before.log`, `browser-before.log`, `unit-after.log`, `browser-after.log`, `build.log`, `docker-build.log`, and the real [before](../../artifacts/metrics-query-fix-2026-11-12/before-deployed.json)/[after](../../artifacts/metrics-query-fix-2026-11-12/after-deployed.json) request/count probes. Retain these separately when sharing the report.

This closes the reproduced query defect and its missing regression coverage. R9's broader correction/input-revision telemetry and the live planning failures remain outstanding. The next implementation job is R3 collection identity and candidate selection; the previous backend/live evaluation results remain the baseline because this change affects only frontend read-query construction.
