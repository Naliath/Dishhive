import { ChangeDetectionStrategy, Component, Inject, OnInit, computed, signal } from '@angular/core';
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
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { CollectionMentionDirective } from '../../directives/collection-mention.directive';
import { MealSuggestionsService } from '../../services/meal-suggestions.service';
import { IntegrationsService } from '../../services/integrations.service';
import { MealSuggestion } from '../../models/meal-suggestion.model';

export interface SuggestionReviewDialogData {
  /** ISO date (yyyy-MM-dd) of the week's Monday */
  weekStart: string;
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
    MatProgressSpinnerModule,
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
  readonly suggestions = signal<MealSuggestion[]>([]);
  readonly selectedIndexes = signal<Set<number>>(new Set());

  readonly selectedCount = computed(() => this.selectedIndexes().size);

  instructions = '';

  constructor(
    @Inject(MAT_DIALOG_DATA) private data: SuggestionReviewDialogData,
    private dialogRef: MatDialogRef<SuggestionReviewDialog, MealSuggestion[]>,
    private suggestionsService: MealSuggestionsService,
    private integrationsService: IntegrationsService
  ) {}

  ngOnInit(): void {
    // Live AI check (errors read as "down"): reachable → ask for instructions
    // first; unreachable → the rules fallback runs, so generate immediately
    this.integrationsService.getStatus().subscribe(status => {
      const aiUp = status?.ai.reachable ?? false;
      this.aiAvailable.set(aiUp);
      if (aiUp) {
        this.phase.set('compose');
      } else {
        this.fetch();
      }
    });
  }

  /** (Re-)request suggestions, passing along the current instructions */
  fetch(): void {
    this.phase.set('generating');
    this.failed.set(false);
    this.suggestionsService.suggestWeek(this.data.weekStart, this.instructions).subscribe({
      next: result => {
        this.suggestions.set(result.suggestions);
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
    this.dialogRef.close(selected);
  }
}
