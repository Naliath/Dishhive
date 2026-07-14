---
name: style-dishhive-angular-ui
description: Keep Dishhive's Angular Material 3 interface visually consistent while adding, auditing, or refactoring styles. Use when changing Angular templates, component or page SCSS, global styles, themes, spacing, typography, responsive layouts, exact pixel values, shared utility classes, Material overrides, cards, forms, toolbars, tables, dialogs, loaders, or other rendered UI in src/dishhive-web; also use for CSS cleanup and visual regression work.
---

# Style Dishhive Angular UI

Apply Material-first styling, reuse established design decisions, and prove the rendered result at mobile and desktop widths. Treat CSS reduction as a means to improve consistency and maintainability, not as an end in itself.

## Establish scope and impact

1. Run `git status --short` and preserve unrelated work.
2. Read the affected template and SCSS together. Inspect the component TypeScript when state, conditional content, or Material configuration affects rendering.
3. Search `src/styles.scss`, `src/theme.scss`, nearby components, and templates for an existing Material component, class, token, or layout pattern before adding CSS.
4. Find every consumer before changing a shared component or global selector. A locally small edit can have application-wide impact.
5. Classify the change:
   - **Local:** one component and one rendered surface.
   - **Shared:** a reused component, utility, or repeated semantic pattern.
   - **Global:** theme, density, typography, page gutters, content widths, breakpoints, or Material overrides.
6. Capture a rendered baseline before shared or global work. For a local change, capture the affected state unless the task is demonstrably nonvisual.

Ask the user before choosing among materially different visual outcomes. In particular, ask before changing global density, typography, shape, page width, breakpoint behavior, or component hierarchy; replacing a familiar control with a different Material component; or consolidating selectors whose visual similarity may hide different semantics. State which screens will change and what decision is needed. Do not ask when the requested outcome already determines the choice and the change is safely local.

## Make Material the source of truth

- Prefer Angular Material components and their public configuration over custom equivalents.
- Prefer Material theme APIs and `--mat-sys-*` tokens for color, typography, elevation, and shape.
- Keep palette seed values and Material theme configuration in theme files. Do not add hex, RGB, or HSL colors to component SCSS, including fallback colors inside `var()`.
- Use Material typography classes in templates for ordinary headings, body text, and labels. Keep exact icon and illustration geometry local when it is intrinsic to the asset or affordance.
- Use Material button, card, list, chip, form-field, dialog, and interaction states. Do not recreate hover, focus, disabled, ripple, elevation, or button padding unless a verified requirement is not supported by the public API.
- Avoid `::ng-deep`, `.mat-mdc-*`, and `.mdc-*`. If a public Material override mixin or component input cannot solve the problem, document why the internal override is necessary and test it as an upgrade risk.

Consult `docs/angular/scss-guidelines.md` and `docs/angular/best-practices.md` when a policy detail is unclear. Resolve their shorthand rules with the judgment below rather than applying them mechanically.

## Reuse classes and tokens deliberately

- Reuse an existing global utility such as `.empty-state` when its semantics match. Verify that a utility has real consumers before retaining or extending it.
- Move a pattern global only when at least three consumers need the same semantic behavior, not merely because several declaration blocks look alike. Use an explicit, application-owned class; avoid broad global element selectors.
- Prefer a shared Angular component when markup and behavior repeat. Prefer a global utility only when the markup remains meaningfully different.
- Keep unique layout rules component-local so Angular's style encapsulation remains useful.
- Use parent `gap` for sibling spacing. Prefer logical properties such as `margin-inline`, `padding-block`, and `inset-inline-end`.
- Prefer intrinsic layouts such as wrapping flexbox and `repeat(auto-fit, minmax(...))`. Use a breakpoint when the layout genuinely changes mode or intrinsic sizing cannot keep controls usable.

Do not replace every number with a variable. Create or extend an application token only for an intentional cross-application decision such as page gutter, section rhythm, standard content width, or minimum control size. Keep a one-off value local when naming it would add indirection without consistency. CSS custom properties cannot be used in media-query conditions; use the established breakpoint consistently or a Sass module when centralizing a breakpoint is justified.

Reuse the established layout tokens from `src/styles.scss`: `--app-page-gutter`, `--app-content-narrow`, `--app-content-reading`, `--app-content-medium`, and `--app-content-wide`. Do not add another content width when one of these roles fits.

## Judge exact values by purpose

Retain exact values when they express:

- one-pixel borders or separators;
- icon, loader, illustration, hit-area, or overlay geometry;
- an intentional content max-width or grid minimum;
- a verified breakpoint;
- clipping, visually hidden content, animation, or image-crop mechanics.

Normalize or remove values when they express:

- arbitrary spacing outside the Material 4px rhythm without visual evidence;
- component text sizes already represented by Material typography;
- repeated page gutters, section gaps, or content widths that should be one design decision;
- hardcoded theme fallbacks;
- custom button, card, or interaction styling already supplied by Material;
- compensating offsets that mask overflow or child sizing defects.

Treat the documented 100-line component-SCSS target as an audit signal. Reduce duplication and move truly shared behavior, but do not split or globalize cohesive component styles just to satisfy a line count.

## Preserve responsive behavior

- Make the component intrinsically responsive first.
- Use the existing 600px mobile boundary for a real mobile layout change unless rendered evidence supports a different component-specific threshold.
- At narrow widths, prefer `min-width: 0`, `minmax(0, 1fr)`, wrapping, and bounded inner scrolling.
- Never hide page-level overflow to conceal a child sizing bug.
- Keep tables complete. Reflow them when the information hierarchy allows it; otherwise provide a clearly bounded and visually understandable horizontal scroller.
- For route-wide or mobile-sensitive work, read `../audit-dishhive-mobile-ui/SKILL.md` and follow its audit procedure.

## Compare the rendered interface

1. Use the running API with representative data and start the Angular app from `src/dishhive-web`.
2. Capture the affected routes, dialogs, and states before and after at 360px and 1280px using the same data and viewport heights.
3. Inspect every screenshot, even when geometry passes. Compare visual hierarchy, density, whitespace, wrapping, card proportions, alignment, scroll affordances, and the Dishhive amber/cyan Material identity.
4. Exercise loading, empty, populated, error, long-text, dialog, and interactive states that the change can affect.
5. Test both light and dark modes when changing colors, theme tokens, elevation, or borders.
6. If a visual difference is not clearly required or an intentional element now feels materially different, pause and request user validation with the before/after evidence.

For broad scans, run:

```powershell
powershell -ExecutionPolicy Bypass -File ".agents/skills/audit-dishhive-mobile-ui/scripts/run-audit.ps1"
```

Use separate output directories for before and after captures. Pass seeded recipe routes through `-AdditionalRoutes` when changing recipe detail or edit surfaces.

## Verify and report

Run from `src/dishhive-web`:

```powershell
npm run build
npm test -- --watch=false
```

Then run `git diff --check`. Report:

- what remained custom and why;
- what reused Material, a global token, a utility, or a shared component;
- which exact values remain intentional;
- the strongest before/after rendered evidence at 360px and 1280px;
- any state or visual decision that still needs user validation.
