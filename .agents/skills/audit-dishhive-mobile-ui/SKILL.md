---
name: audit-dishhive-mobile-ui
description: Audit and fix Dishhive Angular pages and Material dialogs for mobile usability at a 360px viewport while preserving the desktop experience. Use when adding or changing a Dishhive page, dialog, toolbar, table, form, card grid, loader, or responsive SCSS; when a screenshot shows clipped controls, phantom scrollbars, horizontal overflow, or cramped content; or when asked for a visual mobile regression scan.
---

# Audit Dishhive Mobile UI

Audit the rendered application at 360px, fix mobile-only regressions, and retain desktop behavior. Treat screenshots and browser geometry as evidence; do not rely on static SCSS inspection alone.

## Run the audit

1. Work from the Dishhive repository root.
2. Inspect `git status --short` and preserve unrelated changes.
3. Ensure the API is available and start the Angular dev server from `src/dishhive-web` with `npm start -- --host 127.0.0.1`. Its configured port is 4300.
4. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File ".agents/skills/audit-dishhive-mobile-ui/scripts/run-audit.ps1"
   ```

   Pass `-BaseUrl`, `-OutputDir`, `-Width`, or `-Height` when needed. The script discovers static routes from `app.routes.ts`, captures full-page screenshots, probes known dialogs, and writes `audit-report.json`. Parameterized routes are listed as skipped; exercise them with real seeded IDs through `-AdditionalRoutes 'recipes/123,recipes/123/edit'`.
5. Read the report. Investigate every page where `documentWidth` exceeds `viewportWidth`, every `outsideViewport` entry, every failed route, and every dialog whose content `scrollWidth` exceeds `clientWidth`.
6. Open every screenshot with the local image viewer. Check visual balance even when the geometry report passes.

## Inspect each rendered surface

At 360px verify:

- All primary actions remain visible and tappable without page-level horizontal scrolling.
- Headers, week navigation, filters, cards, form fields, button labels, chips, and long translated text wrap coherently.
- Tables either reflow or scroll inside an obvious bounded container; the page itself must not clip columns.
- Material dialogs fit within the viewport. Loading and empty states must not inherit content minimum widths that create phantom scrollbars.
- Dialog action buttons remain visible; stack fields and actions when a horizontal row becomes cramped.
- Loaders stay within their SVG and host bounds, including animated transforms.
- Fixed or minimum widths account for Material dialog content padding. Prefer `min-width: 0`, `width: 100%`, and `box-sizing: border-box` inside mobile media queries.
- Off-canvas sidenav geometry is not a regression while the sidenav is closed.

Also exercise stateful UI that screenshots of initial routes miss: loading, empty, populated, long-content, error, and dialog states. Search for `MatDialog`, `dialog.open`, conditional templates, tables, and loaders when a changed page introduces a new state. Add a safe dialog probe to `scripts/audit-mobile-ui.cjs` when a new recurring dialog can be opened without mutating data.

## Fix responsively

Scope changes to `@media (max-width: 600px)` unless the defect is intrinsically size-independent. Preserve the existing desktop layout and source hierarchy.

Prefer:

- Grid areas or explicit classes for multi-row mobile toolbars.
- `minmax(0, 1fr)`, `min-width: 0`, wrapping, and bounded inner scrolling.
- Component-local rules over global Material overrides.
- A scroll wrapper around data tables when every column must remain available.

Do not mask a child sizing bug with global `overflow-x: hidden`; prove which element exceeds its container and fix that element.

## Verify the result

1. Re-run the audit at 360px and confirm the report and screenshots.
2. Re-run affected interactions manually with the browser automation path when the state changes too quickly for a probe.
3. Check representative pages and dialogs at 1280px. Mobile fixes must not shrink or rearrange the desktop experience.
4. Run from `src/dishhive-web`:

   ```powershell
   npm run build
   npm test -- --watch=false
   ```

5. Run `git diff --check` and report the strongest rendered and automated evidence. Explicitly name any page or state that could not be exercised because its data or API was unavailable.

The audit script installs `playwright-core` only into a temporary cache and uses the locally installed Chrome channel. It does not change project dependencies.
