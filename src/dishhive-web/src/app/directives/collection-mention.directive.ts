import { AfterViewInit, Directive, ElementRef, HostListener, OnInit, computed, effect, inject, signal } from '@angular/core';
import { MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { CookbooksService } from '../services/cookbooks.service';
import { Cookbook } from '../models/recipe.model';
import { applyMention, findActiveMention } from './collection-mention.util';

/**
 * Caret-aware #[Collection Name] autocomplete for instruction inputs. Attach to a
 * single-line input together with a mat-autocomplete whose options come from
 * `suggestions()` and use `valueFor(...)` as the option value:
 *
 *   <input matInput [(ngModel)]="..." appCollectionMention #mention="appCollectionMention"
 *          [matAutocomplete]="auto">
 *   <mat-autocomplete #auto>
 *     @for (c of mention.suggestions(); track c.id) {
 *       <mat-option [value]="mention.valueFor(c)">…</mat-option>
 *     }
 *   </mat-autocomplete>
 *
 * Typing "#ea" suggests matching collections; selecting one replaces the partial
 * mention with the complete "#[Easy Weekday Dishes] " token. The option value is
 * the WHOLE replacement text because mat-autocomplete overwrites the full input
 * value on selection.
 */
@Directive({
  selector: 'input[appCollectionMention], textarea[appCollectionMention]',
  standalone: true,
  exportAs: 'appCollectionMention'
})
export class CollectionMentionDirective implements OnInit, AfterViewInit {
  private readonly el = inject<ElementRef<HTMLInputElement | HTMLTextAreaElement>>(ElementRef);
  private readonly trigger = inject(MatAutocompleteTrigger, { self: true });
  private readonly cookbooksService = inject(CookbooksService);

  private readonly cookbooks = signal<Cookbook[]>([]);
  private readonly active = signal<ReturnType<typeof findActiveMention>>(null);
  /** Caret position after the most recently rendered replacement value */
  private insertCaret = 0;

  readonly suggestions = computed(() => {
    const mention = this.active();
    if (!mention) {
      return [];
    }
    const query = mention.query.trim().toLowerCase();
    return this.cookbooks().filter(c => c.name.toLowerCase().includes(query));
  });

  constructor() {
    // The panel only makes sense while an in-progress mention has matches
    effect(() => {
      if (this.suggestions().length > 0) {
        this.trigger.openPanel();
      } else {
        this.trigger.closePanel();
      }
    });
  }

  /** Whether the suggestion panel is open (hosts use this to gate Enter handlers) */
  get panelOpen(): boolean {
    return this.trigger.panelOpen;
  }

  ngOnInit(): void {
    this.cookbooksService.getCookbooks().subscribe({
      next: cookbooks => this.cookbooks.set(cookbooks),
      error: () => { /* mentions degrade to plain text */ }
    });
  }

  ngAfterViewInit(): void {
    this.trigger.autocomplete?.optionSelected.subscribe(() => {
      // Material wrote the full replacement value; put the caret after the token
      setTimeout(() => {
        this.el.nativeElement.setSelectionRange(this.insertCaret, this.insertCaret);
        this.active.set(null);
        this.trigger.closePanel();
      });
    });
  }

  /** The full input value with this collection's token replacing the typed mention */
  valueFor(cookbook: Cookbook): string {
    const input = this.el.nativeElement;
    const mention = this.active();
    if (!mention) {
      return input.value;
    }
    const caret = input.selectionStart ?? input.value.length;
    const applied = applyMention(input.value, caret, mention.start, cookbook.name);
    this.insertCaret = applied.caret;
    return applied.text;
  }

  @HostListener('input')
  @HostListener('click')
  @HostListener('keyup.arrowleft')
  @HostListener('keyup.arrowright')
  refreshMention(): void {
    const input = this.el.nativeElement;
    this.active.set(findActiveMention(input.value, input.selectionStart ?? input.value.length));
  }

  @HostListener('blur')
  onBlur(): void {
    // Delayed so an option click still lands before the mention state resets
    setTimeout(() => this.active.set(null), 200);
  }
}
