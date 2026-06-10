# SCSS/Styling Guidelines

## Overview
This project uses **Material Design 3** with a custom theme system. All styling should leverage Material's built-in components and design tokens to ensure consistency across light and dark themes.

## Core Principles

### 1. Material Components First
**Always use Material components** before writing custom CSS:
- `<mat-card>` for containers (automatic elevation, theming)
- `<mat-list>` for lists (proper spacing, accessibility)
- `<mat-button>` variants (proper states, ripples)
- Material handles: elevation, shadows, hover/focus states, animations

### 2. No Hardcoded Colors
**NEVER use hardcoded color values:**
```scss
❌ BAD
color: #333333;
background: rgb(255, 255, 255);
border: 1px solid rgba(0, 0, 0, 0.12);

✅ GOOD
color: var(--mat-sys-on-surface);
background: var(--mat-sys-surface);
border: 1px solid var(--mat-sys-outline-variant);
```

### 3. Centralize Common Patterns
If a style pattern appears in **3+ components**, move it to `styles.scss`:
```scss
// In styles.scss
.loading-spinner {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 200px;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 24px;
}
```

### 4. Modern CSS Over Media Queries
Use responsive CSS patterns instead of explicit breakpoints:
```scss
❌ BAD - Media queries
.grid {
  grid-template-columns: repeat(3, 1fr);
  
  @media (max-width: 768px) {
    grid-template-columns: repeat(2, 1fr);
  }
  
  @media (max-width: 480px) {
    grid-template-columns: 1fr;
  }
}

✅ GOOD - Responsive without media queries
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(300px, 100%), 1fr));
  gap: 16px;
}
```

### 5. Minimal Component SCSS
Page-specific SCSS files should be **< 100 lines**. Only include:
- Unique layout patterns for that page
- Component-specific overrides (rare)
- No duplicate utility classes
- No common patterns (use global styles)

## Material Design 3 Color Tokens

### Surface Colors
```scss
var(--mat-sys-surface)                // Base surface (white/dark)
var(--mat-sys-surface-container)      // Card backgrounds
var(--mat-sys-surface-container-high) // Elevated cards
var(--mat-sys-surface-bright)         // Brightest surface variant
var(--mat-sys-primary-container)      // Primary colored surfaces
var(--mat-sys-error-container)        // Error state backgrounds
var(--mat-sys-tertiary-container)     // Warning state backgrounds
```

### Text Colors
```scss
var(--mat-sys-on-surface)             // Primary text (high contrast)
var(--mat-sys-on-surface-variant)     // Secondary text (medium contrast)
var(--mat-sys-on-primary)             // Text on primary colored backgrounds
var(--mat-sys-on-error)               // Text on error backgrounds
var(--mat-sys-on-error-container)     // Text on error containers
var(--mat-sys-on-tertiary-container)  // Text on warning containers
```

### Semantic Colors
```scss
var(--mat-sys-primary)                // Primary brand color (actions, links)
var(--mat-sys-error)                  // Errors, destructive actions, expired items
var(--mat-sys-tertiary)               // Warnings, expiring soon items
var(--mat-sys-outline)                // Standard borders
var(--mat-sys-outline-variant)        // Subtle borders, dividers
var(--mat-sys-scrim)                  // Overlays (black in light, white in dark)
var(--mat-sys-shadow)                 // Shadow colors
```

## Layout Patterns

### Container Max-Width
```scss
.page-container {
  max-width: 600px;   // Forms, narrow content
  max-width: 800px;   // Medium content, lists
  max-width: 1200px;  // Wide content, grids
  margin-inline: auto; // Modern logical property
}
```

### Responsive Grid
```scss
.items-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(300px, 100%), 1fr));
  gap: 16px; // Material 8px base unit × 2
}
```

### Flex Layouts
```scss
// Horizontal layout with wrapping
.row {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  
  > * {
    flex: 1;
    min-width: min(200px, 100%); // Responsive without media query
  }
}

// Vertical layout with consistent spacing
.column {
  display: flex;
  flex-direction: column;
  gap: 8px; // Use gap instead of margins
}
```

### Centered Content
```scss
.centered {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 200px;
}
```

## Spacing Guidelines

### Material Spacing Scale
Use multiples of **4px** (Material's base unit):
```scss
gap: 4px;   // Tight spacing
gap: 8px;   // Standard spacing (most common)
gap: 12px;  // Medium spacing
gap: 16px;  // Large spacing (most common)
gap: 24px;  // Extra large spacing
gap: 32px;  // Section spacing
```

### Responsive Spacing
```scss
// Using clamp() for fluid spacing
padding: clamp(8px, 2vw, 16px);
gap: clamp(12px, 3vw, 24px);
```

### Use Gap Over Margins
```scss
❌ BAD - Individual margins
.item {
  margin-bottom: 16px;
}
.item:last-child {
  margin-bottom: 0;
}

✅ GOOD - Gap for consistent spacing
.container {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
```

## Modern CSS Features

### Logical Properties (Internationalization)
```scss
❌ BAD - Physical properties
margin: 0 auto;
padding-top: 16px;
border-left: 2px solid;
text-align: left;

✅ GOOD - Logical properties
margin-inline: auto;
padding-block-start: 16px;
border-inline-start: 2px solid;
text-align: start;
```

### Color Mixing (Semi-Transparent Colors)
```scss
// Semi-transparent overlay
background: color-mix(in srgb, var(--mat-sys-scrim) 80%, transparent);

// Tinted surface
background: color-mix(in srgb, var(--mat-sys-primary) 15%, var(--mat-sys-surface));
```

### Container Queries (Future)
Material 3 supports container queries for responsive components:
```scss
@container (min-width: 400px) {
  .card { grid-template-columns: 1fr 1fr; }
}
```

## Typography

### Use Material Typography Classes
```html
<!-- ❌ BAD: Custom font sizing -->
<p style="font-size: 14px; color: #666;">Small text</p>

<!-- ✅ GOOD: Material typography class -->
<p class="mat-body-small" style="color: var(--mat-sys-on-surface-variant);">Small text</p>
```

### Material Typography Scale
```html
<h1 class="mat-headline-large">Large headline</h1>
<h2 class="mat-headline-medium">Medium headline</h2>
<h3 class="mat-headline-small">Small headline</h3>
<p class="mat-body-large">Large body text</p>
<p class="mat-body-medium">Default body text</p>
<p class="mat-body-small">Small body text</p>
<span class="mat-label-large">Large label</span>
<span class="mat-label-medium">Medium label</span>
<span class="mat-label-small">Small label</span>
```

### When to Use Custom Typography
Only override when Material doesn't provide:
```scss
.special-title {
  font-family: inherit; // Always inherit, never hardcode
  font-size: 1.75rem;
  font-weight: 500;
  color: var(--mat-sys-on-surface); // Always use theme variables
}
```

## What NOT to Style

### Don't Override Material Internals
```scss
❌ BAD - Overriding Material component internals
.mat-mdc-card {
  box-shadow: 0 4px 8px rgba(0,0,0,0.1) !important;
}

.mat-mdc-button {
  border-radius: 20px !important;
}

✅ GOOD - Use Material's built-in variants
<mat-card appearance="outlined">...</mat-card>
<button mat-button>...</button>
<button mat-raised-button>...</button>
<button mat-fab>...</button>
```

### Don't Create Custom Buttons
```scss
❌ BAD - Custom button styles
.custom-button {
  background: #2196f3;
  color: white;
  padding: 12px 24px;
  border-radius: 4px;
  
  &:hover {
    background: #1976d2;
  }
}

✅ GOOD - Material button variants
<button mat-raised-button color="primary">Primary</button>
<button mat-flat-button color="accent">Accent</button>
<button mat-stroked-button>Outlined</button>
```

### Don't Add Custom Focus/Hover States
Material provides proper interaction states automatically:
- Ripple effects
- Focus indicators
- Hover states
- Active states
- Disabled states

## File Structure

### Component SCSS Template
```scss
// 1. Page container with max-width
.page-container {
  max-width: 800px;
  margin-inline: auto;
}

// 2. Unique layout patterns for this page
.custom-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(200px, 100%), 1fr));
  gap: 12px;
}

// 3. Minimal component-specific overrides (rare)
.special-card {
  border-radius: 12px; // Only if Material default doesn't work
}

// 4. Print styles if needed
@media print {
  .no-print { display: none; }
}
```

## Examples

### Good Component SCSS
```scss
// item-list.scss - 47 lines total
.item-list-container {
  max-width: 1200px;
  margin-inline: auto;
}

.items-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(300px, 100%), 1fr));
  gap: 16px;
}

.item-details {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.expiration-row {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--mat-sys-on-surface-variant);
  
  mat-icon {
    font-size: 18px;
    width: 18px;
    height: 18px;
  }
}
```

### Bad Component SCSS
```scss
// ❌ DON'T DO THIS
.item-list-container {
  max-width: 1200px;
  margin: 0 auto;
  background: #ffffff;
}

.loading-spinner { // Duplicate - should be in styles.scss
  display: flex;
  justify-content: center;
  padding: 40px;
}

.empty-state { // Duplicate - should be in styles.scss
  text-align: center;
  padding: 40px;
  color: #999999;
}

h2 {
  font-size: 24px;
  font-weight: bold;
  color: #333333;
  margin-bottom: 20px;
}

.items-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
  
  @media (max-width: 768px) {
    grid-template-columns: repeat(2, 1fr);
  }
  
  @media (max-width: 480px) {
    grid-template-columns: 1fr;
  }
}

.card-title {
  font-size: 18px;
  color: #000000;
}

.card-subtitle {
  font-size: 14px;
  color: #666666;
}
```

## Checklist Before Committing SCSS

- [ ] No hardcoded colors (#hex, rgb(), rgba())
- [ ] All colors use `var(--mat-sys-*)` tokens
- [ ] No duplicate utility classes (check if pattern exists in styles.scss)
- [ ] Using modern CSS (gap, logical properties, responsive grids)
- [ ] Minimal media queries (prefer responsive CSS)
- [ ] Using Material typography classes where possible
- [ ] File is < 100 lines (if longer, refactor common patterns)
- [ ] No Material component internal overrides
- [ ] Tested in both light and dark themes

## Quick Reference

### Common Replacements
```scss
// Colors
#ffffff → var(--mat-sys-surface)
#333333 → var(--mat-sys-on-surface)
#666666 → var(--mat-sys-on-surface-variant)
#f44336 → var(--mat-sys-error)
#2196f3 → var(--mat-sys-primary)
rgba(0,0,0,0.12) → var(--mat-sys-outline-variant)
rgba(0,0,0,0.6) → var(--mat-sys-on-surface-variant)

// Spacing
margin: 0 auto → margin-inline: auto
padding-top: 16px → padding-block-start: 16px
margin-bottom: 8px → use gap: 8px on parent instead

// Layout
@media (max-width: 600px) → Use responsive grid patterns
border-left → border-inline-start
text-align: left → text-align: start
```

## Resources
- [Material Design 3](https://m3.material.io/)
- [Angular Material Components](https://material.angular.io/components)
- [CSS Logical Properties](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_Logical_Properties)
- [CSS Grid Layout](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_Grid_Layout)
