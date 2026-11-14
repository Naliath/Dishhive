# Local UI and operational verification - 13 November 2026

Later complete-suite and recovery evidence for the final source is in the
[source/facts followup](final-source-facts-closure-2026-11-13.md). Counts and image
identities below describe this earlier completed operations job.

Queue item 3 is implemented and verified locally. The changes cover quick-plan and
collection controls, production PWA freshness, the household calendar, application
vocabulary and a complete database restore. No hosted CI is required or added.

## Behavior available for review

Collections use Material chip selection/removal and a Material creation field.
Long user names retain their full accessible name and tooltip while the visible
label fits narrow screens. Keyboard selection and removal are covered. Quick-plan
uses Material course controls and retains its native date buttons because each
button contains useful planned-meal context. The date list scrolls inside the
dialog; Enter selects the correct date and Escape restores focus to Plan now.

Application-owned vocabulary, Material paginator labels, notifications, planning
issues and canonical diet/allergen labels are available in English and Dutch.
Recipe text, source categories/units, member names and manually named collections
remain user/source content. Backend issue localization uses stable keys and typed
values, rather than matching an English diagnostic string. The local catalog check
validates key/parameter parity and scans all 22 Angular templates for unlocalized
static text and accessibility labels.

`Household:TimeZone` defaults to `Europe/Brussels` and rejects invalid zones at
startup. An injectable server clock defines calendar dates consistently across
planning, reservations, history, statistics and MCP. Preferences expose the zone
to the browser; its clock uses that zone and updates open views at midnight.
Event timestamps remain UTC. Midnight, daylight-saving and a different browser
zone are covered by tests.

API read caching is now explicit application behavior. Successful allowlisted
GETs are saved with their fetch time. Network errors, server failures and an
eight-second timeout can use that saved response for up to seven days. A banner
shows its saved time even when the browser reports being online. Authorization,
not-found and validation responses do not become stale successes. Successful
mutations invalidate saved reads, including protection against an older in-flight
GET repopulating them. Navigation resets visible cache provenance. Writes fail
offline and are never queued for later submission.

Angular's worker caches the versioned app shell/catalogs and versioned image URLs.
API calls bypass worker data caching so an old worker cannot invisibly return stale
API data. Image URLs change after image updates. App updates offer a reload, which
loads a coherent new shell and catalogs.

## Local verification

The complete run in `artifacts/local-operations-2026-11-13/local-approved/` passed:

| Gate | Result |
| --- | --- |
| Backend, including PostgreSQL and real API/Chrome workflow | 628 passed; zero skipped |
| Frontend | 84 passed in 19 files |
| Browser fixtures | 28 passed |
| Production PWA | One full service-worker lifecycle test passed |
| Linux scraper | Nine passed, with network disabled |
| Generated contracts, fixture types, catalogs and production build | Passed |

These counts describe that complete run. Later source-discovery changes and their
final combined run are recorded in the [current checklist](application-review-status-2026-11-12.md).
The restored package metadata emitted NU1900 because the NuGet vulnerability feed
was unavailable; no dependencies were added to suppress that warning.

The production PWA test exercises a real registered Angular worker against a local
fixture HTTP server: HTTP 503 while online, offline reload, successful quick-plan
write invalidation, changed image URLs, rejected offline writes without later
replay, online recovery and a real second manifest/shell update followed by reload.
It does not depend on a mock service-worker API.

Rendered collection and quick-plan screenshots were inspected at 360px and 1280px
in both languages. Long titles, chips and dialog actions fit; the last quick-plan
date remains keyboard reachable through the internal scroll region. Saved-data
banners were also inspected at both widths in light and dark themes. Geometry and
page-error assertions accompany those captures.

- [Collections, mobile](../../src/dishhive-web/e2e/ui-regression.spec.ts-snapshots/collections-360.png)
- [Collections, Dutch desktop](../../src/dishhive-web/e2e/ui-regression.spec.ts-snapshots/collections-nl-1280.png)
- [Quick-plan, mobile](../../src/dishhive-web/e2e/ui-regression.spec.ts-snapshots/quick-plan-360.png)
- [Quick-plan, Dutch desktop](../../src/dishhive-web/e2e/ui-regression.spec.ts-snapshots/quick-plan-nl-1280.png)
- PWA captures: `artifacts/local-operations-2026-11-13/local-approved/pwa/`
- Before captures: `artifacts/local-operations-2026-11-13/before/`

## Full recovery rehearsal

Run `./scripts/rehearse-local-restore.ps1` to back up the local Dishhive database
and restore a disposable clone. `-Image <tag-or-id>` additionally rehearses startup
with a candidate image. The script pauses the application briefly during backup,
copies the binary PostgreSQL dump without text redirection, and compares complete
row digests/counts for every public table, including image bytes and migration
history. It starts the restored application with background/external work disabled,
reads the recipes, members, plans and preferences APIs, verifies startup did not
change persisted rows, then removes only the generated clone resources.

The final rehearsal used image
`sha256:ab268f893919e23c7d251b383c49025aaa003e19102efd1ee2e8b38e2b235bec`.
All **25 tables, 24 recipes, four members and 14 meals** matched exactly and the
restored application started successfully. Evidence, including the binary dump,
is in `artifacts/local-operations-2026-11-13/restore-final/`.

The dump covers database-backed plans, members, ratings, settings, recipes, images,
enrichment state and metrics. Environment credentials and external services require
their own configuration/backup; Freezy is a separate application and was not changed.
This is a local restore and migration-startup rehearsal. It does not establish a
remote deployment or a real scraper package-update rollback.

## Final combined verification

After the source/facts followups, the final complete command passed **653 backend
checks, zero skipped; 84 frontend tests; 28 fixture browser checks plus the real
API/Chrome workflow; the production PWA lifecycle test; and nine Linux scraper tests**.
All generated-contract, catalog, fixture type and production build checks passed.
Evidence: `artifacts/local-operations-2026-11-13/local-verified/`.

The deployed app also passed read-only collection and quick-plan checks at both
360px and 1280px. Final captures wait for entrance animations to finish; neither
page nor dialog overflowed and no page errors occurred. These captures use the
actual populated development API, separately from the EN/NL fixture baselines.
Evidence: `artifacts/local-operations-2026-11-13/deployed/`.

The exact final image was then rehearsed again after live evaluation:
`sha256:c6191f99613803db10a8e15365c6242b5389c20578a7534380f401e2aa86dd6f`.
All 25 tables, 24 recipes, four members and 14 meals matched, with successful
migration/startup and API checks. Evidence: `artifacts/local-operations-2026-11-13/restore-verified/`.
Normal Freezy integration, background work and disabled diagnostic retention were
restored afterward; the application is healthy at http://localhost:5100.
