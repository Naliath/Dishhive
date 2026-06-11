import { ChangeDetectionStrategy, Component, Inject, OnInit, computed, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MealSuggestionsService } from '../../services/meal-suggestions.service';
import { MealSuggestion } from '../../models/meal-suggestion.model';

export interface SuggestionReviewDialogData {
  /** ISO date (yyyy-MM-dd) of the week's Monday */
  weekStart: string;
}

/**
 * Fetches AI week-plan suggestions and lets the user review them: each proposed
 * dinner is a row with a checkbox (default on). "Add selected" returns the chosen
 * suggestions; the planner page performs the actual creation.
 */
@Component({
  selector: 'app-suggestion-review-dialog',
  standalone: true,
  imports: [
    DatePipe,
    MatDialogModule,
    MatButtonModule,
    MatCheckboxModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './suggestion-review-dialog.html',
  styleUrl: './suggestion-review-dialog.scss'
})
export class SuggestionReviewDialog implements OnInit {
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly suggestions = signal<MealSuggestion[]>([]);
  readonly selectedDates = signal<Set<string>>(new Set());

  readonly selectedCount = computed(() => this.selectedDates().size);

  constructor(
    @Inject(MAT_DIALOG_DATA) private data: SuggestionReviewDialogData,
    private dialogRef: MatDialogRef<SuggestionReviewDialog, MealSuggestion[]>,
    private suggestionsService: MealSuggestionsService
  ) {}

  ngOnInit(): void {
    this.suggestionsService.suggestWeek(this.data.weekStart).subscribe({
      next: result => {
        this.suggestions.set(result.suggestions);
        this.selectedDates.set(new Set(result.suggestions.map(s => s.date)));
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      }
    });
  }

  isSelected(suggestion: MealSuggestion): boolean {
    return this.selectedDates().has(suggestion.date);
  }

  toggle(suggestion: MealSuggestion): void {
    this.selectedDates.update(dates => {
      const next = new Set(dates);
      if (next.has(suggestion.date)) {
        next.delete(suggestion.date);
      } else {
        next.add(suggestion.date);
      }
      return next;
    });
  }

  addSelected(): void {
    const selected = this.suggestions().filter(s => this.isSelected(s));
    this.dialogRef.close(selected);
  }
}
