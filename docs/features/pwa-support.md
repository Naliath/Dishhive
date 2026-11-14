# Feature: PWA Support

Dishhive supports installation, app updates and saved read access to the planner,
shopping list, recipes and reference data. Writes require a connection.

## Cache behavior

- Angular's production service worker owns the versioned app shell, both language
  catalogs, icons and lazily cached fonts/images. It is disabled in development builds.
- `OfflineReadCacheService` owns allowlisted API GET responses and their saved time.
  Reads try the network first. Network errors, HTTP 5xx and an eight-second timeout
  can fall back to a saved response for up to seven days, with a 300-response limit.
- A visible banner identifies the saved-data time. It also appears during API outages
  while the browser still reports being online. Fresh recovery clears the indication;
  navigation resets provenance so it belongs to the current view.
- HTTP 401, 403, 404 and validation errors do not fall back to stale responses.
- Successful mutations invalidate saved reads. Generation guards prevent an earlier
  in-flight read from putting pre-mutation data back in the cache.
- API requests bypass service-worker data caching, including workers installed by an
  earlier app version. Integration status and AI POSTs are never cached as reads.
- Recipe image URLs include a version derived from the recipe update timestamp.
  The worker's image cache respects that query value, so replacements use a new key.
- No offline write queue exists. Failed edits/planning submissions are not replayed
  when the connection returns.

## Updates and installation

`PwaService` checks for updates when stable and every six hours. A ready version offers
a reload; the new page loads its matching shell, chunks and language catalogs. The
service also localizes connectivity/update notices, handles unrecoverable versions,
and captures the browser installation prompt for the Settings install card.

Service workers require HTTPS or localhost. Installation availability depends on
the browser. Source recipe text and user content are not translated by the cache.

## Verification

`scripts/verify-local.ps1` runs the production PWA lifecycle test as part of local
verification. It registers the real Angular worker against a fixture HTTP server,
tests online HTTP 503, saved-data age, offline reload, mutation/image invalidation,
offline write failure without replay, recovery and an actual changed manifest/shell
update followed by reload. Mobile/desktop light/dark captures and geometry checks
cover the saved-data banner. Unit tests cover authorization failures, cache expiry,
late reads after writes and navigation provenance.

See [local UI and operations evidence](../reviews/local-operations-fix-2026-11-13.md)
for the full recovery and calendar/localization checks.
