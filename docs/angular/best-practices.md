# Angular Best Practices for This Project

## Component Architecture
- Use standalone components (Angular 21 default)
- Implement OnPush change detection for performance
- Use signals for reactive state management
- Keep components focused and single-responsibility

## Service Patterns
- Use `providedIn: 'root'` for singleton services
- Implement proper error handling with catchError
- Use RxJS operators for data transformation
- Cache API responses when appropriate

## Material Design
- Follow Material Design 3 guidelines
- Use mat-card for content containers
- Implement proper accessibility (aria labels)
- Use theming system for consistent colors

## Styling & SCSS Best Practices

### Use Material Design Components First
- Leverage Material's built-in layout system (mat-card, mat-list, etc.)
- Material components handle elevation, shadows, and theming automatically
- Only add custom CSS when Material doesn't provide the functionality

### Color Usage
- **NEVER use hardcoded colors** (#hex, rgb(), rgba())
- Always use Material Design 3 CSS custom properties:
  ```scss
  color: var(--mat-sys-on-surface);           // Primary text
  color: var(--mat-sys-on-surface-variant);   // Secondary text
  background: var(--mat-sys-surface-container); // Card backgrounds
  background: var(--mat-sys-primary);          // Primary actions
  color: var(--mat-sys-error);                 // Error states
  border-color: var(--mat-sys-outline-variant); // Borders
  ```
- For semi-transparent colors, use `color-mix()`:
  ```scss
  background: color-mix(in srgb, var(--mat-sys-scrim) 80%, transparent);
  ```

### Typography
- Use Material typography classes instead of custom font sizing:
  ```html
  <p class="mat-body-large">Large body text</p>
  <p class="mat-body-small">Small body text</p>
  <p class="mat-headline-medium">Medium headline</p>
  ```
- Avoid custom `font-size`, `font-weight` unless absolutely necessary

### Layout Patterns
- **Centralize common patterns** in `styles.scss` (loading spinners, empty states)
- Use modern CSS over media queries:
  ```scss
  // ✅ Good - responsive without media queries
  grid-template-columns: repeat(auto-fill, minmax(min(300px, 100%), 1fr));
  
  // ❌ Avoid - explicit media queries
  @media (max-width: 599px) { grid-template-columns: 1fr; }
  ```
- Use logical properties for internationalization:
  ```scss
  margin-inline: auto;      // instead of margin: 0 auto
  padding-block-start: 16px; // instead of padding-top: 16px
  border-inline-start: 2px;  // instead of border-left: 2px
  ```

### Spacing
- Use CSS `gap` instead of margins:
  ```scss
  // ✅ Good
  .container { display: flex; gap: 16px; }
  
  // ❌ Avoid
  .item { margin-bottom: 16px; }
  .item:last-child { margin-bottom: 0; }
  ```
- Prefer Material spacing scale: 4px, 8px, 12px, 16px, 24px
- Use `clamp()` for responsive sizing:
  ```scss
  padding: clamp(8px, 2vw, 16px);
  ```

### Component SCSS Files
- Keep page-specific styles minimal (target < 100 lines)
- Only include styles unique to that component
- Remove duplicate utility classes (use global styles instead)
- Structure:
  ```scss
  // 1. Container/layout
  .page-container { max-width: 900px; margin-inline: auto; }
  
  // 2. Component-specific layout
  .custom-grid { display: grid; gap: 16px; }
  
  // 3. Element-specific overrides only when needed
  .special-card { border-radius: 12px; }
  ```

### What NOT to Style
- Don't override Material component internals unnecessarily
- Don't create custom button styles (use Material variants)
- Don't add custom focus/hover states (Material provides these)
- Don't duplicate common patterns across multiple files

### Global Utility Classes (styles.scss)
Create reusable patterns used in 3+ components:
```scss
// Loading states
.loading-spinner { display: flex; justify-content: center; min-height: 200px; }

// Empty states  
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 16px; }
```

### Example: Good vs Bad
```scss
// ❌ BAD - hardcoded colors, custom margins, media queries
.card {
  background: #ffffff;
  color: #666666;
  margin: 0 auto;
  padding: 20px;
  
  @media (max-width: 600px) {
    padding: 10px;
  }
  
  h3 {
    font-size: 18px;
    margin-bottom: 10px;
  }
}

// ✅ GOOD - theme variables, modern CSS, Material typography
.card {
  background: var(--mat-sys-surface-container);
  color: var(--mat-sys-on-surface-variant);
  margin-inline: auto;
  padding: clamp(10px, 2vw, 20px);
}
// Use mat-headline-small class in template instead of custom h3 styling
```

## Forms
- Use reactive forms over template-driven
- Implement proper validation messages
- Use FormBuilder for complex forms
- Handle async validation appropriately

## State Management
- Use signals for local component state
- Consider NgRx for complex global state
- Avoid shared mutable state
- Use immutable data patterns

## Performance
- Implement virtual scrolling for large lists
- Use trackBy with *ngFor
- Lazy load routes and modules
- Optimize bundle size with tree shaking

## PWA Specific
- Always test offline functionality
- Use service worker caching strategies appropriately
- Handle sync failures gracefully
- Test on actual mobile devices for install prompts
