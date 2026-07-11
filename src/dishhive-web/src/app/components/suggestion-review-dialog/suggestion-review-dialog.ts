import { ChangeDetectionStrategy, Component, DestroyRef, Inject, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TextFieldModule } from '@angular/cdk/text-field';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatTooltipModule } from '@angular/material/tooltip';
import { CookingLoaderComponent } from '../cooking-loader/cooking-loader';
import { CollectionMentionDirective } from '../../directives/collection-mention.directive';
import { MealSuggestionsService } from '../../services/meal-suggestions.service';
import { IntegrationsService } from '../../services/integrations.service';
import { MealSuggestion, SuggestionReviewResult } from '../../models/meal-suggestion.model';

export interface SuggestionReviewDialogData {
  /** ISO date (yyyy-MM-dd) of the week's Monday */
  weekStart: string;
}

/** One day's suggestions, grouped for display; indexes point back into suggestions() */
interface SuggestionDayGroup {
  date: string;
  items: { index: number; suggestion: MealSuggestion }[];
}

/**
 * Dialog phases: a quick live AI check decides whether the user first gets to
 * enter instructions ("3 days vegetarian, one fish dish") before generating.
 * When the AI is down — the rules fallback ignores instructions — generation
 * starts right away instead.
 */
type DialogPhase = 'checking' | 'compose' | 'generating' | 'review';

/**
 * Fetches AI week-plan suggestions and lets the user review them: each proposed
 * dish is a row with a checkbox (default on); a day can hold several dishes
 * (e.g. two small leftovers). Instructions can be given up front (when the AI
 * is reachable) and adjusted on regenerate. "Add selected" returns the chosen
 * suggestions; the planner page performs the creation.
 */
@Component({
  selector: 'app-suggestion-review-dialog',
  standalone: true,
  imports: [
    CookingLoaderComponent,
    DatePipe,
    FormsModule,
    TextFieldModule,
    CollectionMentionDirective,
    MatDialogModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './suggestion-review-dialog.html',
  styleUrl: './suggestion-review-dialog.scss'
})
export class SuggestionReviewDialog implements OnInit {
  readonly phase = signal<DialogPhase>('checking');
  readonly failed = signal(false);
  readonly aiAvailable = signal(false);
  readonly webSearchAvailable = signal(false);
  readonly suggestions = signal<MealSuggestion[]>([]);
  readonly selectedIndexes = signal<Set<number>>(new Set());
  /** Recipes kept out of planning for household allergies (visibility for false positives) */
  readonly excludedForAllergies = signal(0);
  readonly batchId = signal<string | null>(null);

  readonly selectedCount = computed(() => this.selectedIndexes().size);

  /** Every proposal came from the rules fallback: the AI never answered, so any typed
   *  instructions were ignored — surfaced as a banner, not just the per-row tags */
  readonly allFromFallback = computed(() =>
    this.suggestions().length > 0 && this.suggestions().every(s => s.fromFallback));

  /** Suggestions grouped by date so the date renders once per day, not once per dish */
  readonly groupedSuggestions = computed<SuggestionDayGroup[]>(() => {
    const groups: SuggestionDayGroup[] = [];
    const byDate = new Map<string, SuggestionDayGroup>();
    this.suggestions().forEach((suggestion, index) => {
      let group = byDate.get(suggestion.date);
      if (!group) {
        group = { date: suggestion.date, items: [] };
        byDate.set(suggestion.date, group);
        groups.push(group);
      }
      group.items.push({ index, suggestion });
    });
    return groups;
  });

  instructions = '';

  private readonly destroyRef = inject(DestroyRef);

  constructor(
    @Inject(MAT_DIALOG_DATA) private data: SuggestionReviewDialogData,
    private dialogRef: MatDialogRef<SuggestionReviewDialog, SuggestionReviewResult>,
    private suggestionsService: MealSuggestionsService,
    private integrationsService: IntegrationsService
  ) {}

  ngOnInit(): void {
    // Live AI check (errors read as "down"): usable → ask for instructions first;
    // down OR failed its model capability test → the rules fallback runs (and it
    // ignores instructions by design), so skip straight to generating instead of
    // collecting wishes that would be silently dropped
    this.integrationsService.getStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(status => {
        const aiUp = (status?.ai.reachable ?? false) && status?.ai.modelTestVerdict !== 'failed';
        this.aiAvailable.set(aiUp);
        this.webSearchAvailable.set(status?.webSearch?.operational ?? false);
        if (aiUp) {
          this.phase.set('compose');
        } else {
          this.fetch();
        }
      });
  }

  /** (Re-)request suggestions, passing along the current instructions.
   *  Unsubscribing on dialog destroy (Cancel, backdrop click, ESC) aborts the
   *  underlying HTTP request, which the backend reads as caller cancellation — so
   *  closing mid-generation actually stops the model call server-side instead of
   *  leaving it running for up to five minutes on the agentic path. */
  fetch(): void {
    this.phase.set('generating');
    this.failed.set(false);
    this.suggestionsService.suggestWeek(this.data.weekStart, this.instructions)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.suggestions.set(result.suggestions);
          this.batchId.set(result.batchId);
          this.excludedForAllergies.set(result.excludedForAllergies ?? 0);
          this.selectedIndexes.set(new Set(result.suggestions.map((_, index) => index)));
          this.phase.set('review');
        },
        error: () => {
          this.failed.set(true);
          this.phase.set('review');
        }
      });
  }

  isSelected(index: number): boolean {
    return this.selectedIndexes().has(index);
  }

  toggle(index: number): void {
    this.selectedIndexes.update(indexes => {
      const next = new Set(indexes);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
  }

  addSelected(): void {
    const selected = this.suggestions().filter((_, index) => this.isSelected(index));
    const batchId = this.batchId();
    if (batchId) {
      this.dialogRef.close({ batchId, suggestions: selected });
    }
  }
}
