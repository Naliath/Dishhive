import { AfterViewInit, Directive, ElementRef, HostListener, OnInit, computed, effect, inject, signal } from '@angular/core';
import { MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { CookbooksService } from '../services/cookbooks.service';
import { RecipeSourcesService } from '../services/recipe-sources.service';
import { Cookbook } from '../models/recipe.model';
import { RecipeSource } from '../models/recipe-source.model';
import { applyMention, findActiveMention } from './collection-mention.util';

/** A unified autocomplete row for either a collection (#) or a source (@) mention */
export interface MentionSuggestion {
  id: string;
  /** Name inserted into the token */
  name: string;
  /** mat-icon name */
  icon: string;
  /** Secondary detail (recipe count, or host) */
  detail?: string;
}

/**
 * Caret-aware mention autocomplete for instruction inputs, handling two triggers:
 * "#[Collection Name]" (recipe collections) and "@[Source]" (external recipe sites).
 * Attach to a single-line input or textarea together with a mat-autocomplete whose
 * options come from `suggestions()` and use `valueFor(...)` as the option value:
 *
 *   <input matInput [(ngModel)]="..." appCollectionMention #mention="appCollectionMention"
 *          [matAutocomplete]="auto">
 *   <mat-autocomplete #auto>
 *     @for (m of mention.suggestions(); track m.id) {
 *       <mat-option [value]="mention.valueFor(m)">…</mat-option>
 *     }
 *   </mat-autocomplete>
 *
 * Typing "#ea" suggests matching collections, "@dag" matching sources; selecting one
 * replaces the partial mention with the complete bracketed token. The option value is
 * the WHOLE replacement text because mat-autocomplete overwrites the full input value.
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
  private readonly sourcesService = inject(RecipeSourcesService);

  private readonly cookbooks = signal<Cookbook[]>([]);
  private readonly sources = signal<RecipeSource[]>([]);
  private readonly active = signal<ReturnType<typeof findActiveMention>>(null);
  /** Caret position after the most recently rendered replacement value */
  private insertCaret = 0;

  readonly suggestions = computed<MentionSuggestion[]>(() => {
    const mention = this.active();
    if (!mention) {
      return [];
    }
    const query = mention.query.trim().toLowerCase();

    if (mention.trigger === '@') {
      return this.sources()
        .filter(s => s.name.toLowerCase().includes(query) || s.host.toLowerCase().includes(query))
        .map(s => ({
          id: s.host,
          name: s.name,
          icon: 'public',
          detail: s.name.toLowerCase() === s.host.toLowerCase() ? undefined : s.host
        }));
    }

    return this.cookbooks()
      .filter(c => c.name.toLowerCase().includes(query))
      .map(c => ({
        id: c.id,
        name: c.name,
        icon: c.kind === 'auto' ? 'auto_awesome' : 'bookmark',
        detail: `${c.recipeCount} recipes`
      }));
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
    this.sourcesService.getSources().subscribe({
      next: sources => this.sources.set(sources),
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

  /** The full input value with this suggestion's token replacing the typed mention */
  valueFor(suggestion: MentionSuggestion): string {
    const input = this.el.nativeElement;
    const mention = this.active();
    if (!mention) {
      return input.value;
    }
    const caret = input.selectionStart ?? input.value.length;
    const applied = applyMention(input.value, caret, mention.start, mention.trigger, suggestion.name);
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
