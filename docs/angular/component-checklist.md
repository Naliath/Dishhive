# Angular Component Checklist

## Every Component Should:
- [ ] Use OnPush change detection if possible
- [ ] Implement OnDestroy and unsubscribe from observables
- [ ] Have proper TypeScript typing (no `any`)
- [ ] Include accessibility attributes (aria-label, role, etc.)
- [ ] Have meaningful component/selector names
- [ ] Document complex logic with comments
- [ ] Handle loading and error states
- [ ] Use Material Design components consistently
- [ ] Follow standalone component pattern
- [ ] Use signals for reactive state when appropriate

## Common Mistakes to Avoid:
- ❌ Don't subscribe in templates (use async pipe)
- ❌ Don't mutate input properties directly
- ❌ Don't forget to unsubscribe from subscriptions
- ❌ Don't use ElementRef for DOM manipulation (use Renderer2)
- ❌ Don't bypass Angular's security (sanitization)
- ❌ Don't create memory leaks with event listeners
- ❌ Don't use `any` type - always provide proper types
- ❌ Don't put business logic in components (use services)

## Component Template Structure
```typescript
@Component({
  selector: 'app-feature-name',
  standalone: true,
  imports: [CommonModule, MaterialModules...],
  templateUrl: './feature-name.component.html',
  styleUrl: './feature-name.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeatureNameComponent implements OnInit, OnDestroy {
  // Signals
  private itemsSignal = signal<Item[]>([]);
  
  // Subscriptions
  private destroy$ = new Subject<void>();
  
  constructor(private service: SomeService) {}
  
  ngOnInit(): void {
    // Subscribe with takeUntil for cleanup
    this.service.getData()
      .pipe(takeUntil(this.destroy$))
      .subscribe(data => this.itemsSignal.set(data));
  }
  
  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
```

## Accessibility Guidelines
- Always include aria-labels for icon-only buttons
- Use semantic HTML elements (button, nav, main, etc.)
- Ensure keyboard navigation works
- Test with screen readers
- Provide focus indicators
- Use proper heading hierarchy (h1, h2, h3)
