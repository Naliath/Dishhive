# Feature: PWA Support

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [shopping-list-export.md](shopping-list-export.md)

## Feature Goal

Make Dishhive installable (home screen / desktop) with offline **read** access to the
week plan and shopping list — check what's for dinner or what to buy while in a store
with bad reception. Follows Freezy's service-worker setup, ending the deliberate
initial deviation recorded in the infrastructure plan §2.

## Scope

**In scope**
- Angular service worker (`@angular/service-worker`, production builds only)
- Web app manifest + PNG icon set rendered from the SVG app icon
- Offline read: cached GET responses for planner, shopping list, recipes and reference
  data (freshness strategy — network first, cache fallback)
- Update notification ("A new version is available" snackbar → reload)
- Online/offline snackbars; install button on the settings page when the browser
  offers an install prompt

**Out of scope**
- Offline **writes** / request queueing (Freezy's offline queue is for in-store
  scanning; Dishhive edits happen at home)
- Web push notifications

## Design

- `ngsw-config.json`:
  - `assetGroups`: app shell prefetched (index, JS, CSS, manifest, icons); images,
    fonts (incl. Google Fonts / Material Icons) cached lazily
  - `dataGroups` (all `freshness`: network with 4s timeout, cache fallback — offline
    serves the last seen data, online stays live):
    - `/api/plannedmeals**` (week plan), `/api/shoppinglist**`
    - `/api/familymembers**`, `/api/settings**`, `/api/cookbooks**`,
      `/api/recipes**`, `/api/statistics**` (page render dependencies)
    - `/api/recipes/*/image` uses `performance` (cache first — images are immutable)
  - `/api/integrations/status` and the AI suggestion endpoints are deliberately
    **not** cached: stale "AI is up" answers are worse than none, and suggestion
    POSTs can't be cached anyway
- `manifest.webmanifest` + `public/icons/dishhive_pwa_*.png` (48–512 px, rendered
  full-bleed from `icon.svg` so `maskable` purposes don't expose transparent corners;
  180 px doubles as the apple-touch-icon)
- `provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy:
  'registerWhenStable:30000' })` in `app.config.ts` — dev server stays SW-free
- `services/pwa.service.ts` (instantiated by the App component for its side effects):
  - checks for updates once stable + every 6 h; `VERSION_READY` → snackbar with
    an Update action (activate + reload); `unrecoverable` → reload snackbar
  - online/offline signals + snackbars
  - captures `beforeinstallprompt`; the settings page shows an "Install app" card
    while an install prompt is available
- The API serves everything from `wwwroot`, so `ngsw-worker.js`, `ngsw.json` and the
  manifest need no backend changes

## Risks / Notes

- The service worker only activates on HTTPS or localhost — fine for home-LAN use via
  localhost or a reverse proxy with TLS; plain `http://hostname` won't register it.
- `freshness` with a 4s timeout means a *slow* (not down) network falls back to cached
  data after 4s; acceptable for read views.
- Updating the deployed container does not interrupt users: the SW serves the old
  version until the update snackbar reload.

## Implementation Checklist

- [x] `@angular/service-worker` + `ngsw-config.json` + `angular.json` production wiring
- [x] `manifest.webmanifest`, PNG icon set, index.html links (manifest, apple-touch-icon)
- [x] `PwaService` (update flow, online/offline, install prompt) wired in `App`
- [x] Settings page install card
- [x] Offline smoke test against the Docker stack
