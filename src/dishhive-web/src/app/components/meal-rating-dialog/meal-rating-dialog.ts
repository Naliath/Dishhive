import { ChangeDetectionStrategy, Component, Inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { FamilyMember } from '../../models/family-member.model';
import { PlannedMeal } from '../../models/planned-meal.model';

export interface MealRatingDialogData {
  meal: PlannedMeal;
  members: FamilyMember[];
}

/**
 * Star-rating dialog for a past meal: one row of 5 tappable stars per family
 * member (attendees first). Each tap saves immediately; tapping the member's
 * current rating clears it. Closing returns the updated meal.
 */
@Component({
  selector: 'app-meal-rating-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatSnackBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './meal-rating-dialog.html',
  styleUrl: './meal-rating-dialog.scss'
})
export class MealRatingDialog {
  readonly meal;

  /** Attendees first, then the rest of the household */
  readonly members: FamilyMember[];

  readonly stars = [1, 2, 3, 4, 5];

  constructor(
    @Inject(MAT_DIALOG_DATA) data: MealRatingDialogData,
    private dialogRef: MatDialogRef<MealRatingDialog, PlannedMeal>,
    private plannedMealsService: PlannedMealsService,
    private snackBar: MatSnackBar
  ) {
    this.meal = signal<PlannedMeal>(data.meal);
    const attendeeIds = new Set(data.meal.attendeeIds);
    this.members = [...data.members].sort((a, b) => {
      const aAttended = attendeeIds.has(a.id) ? 0 : 1;
      const bAttended = attendeeIds.has(b.id) ? 0 : 1;
      return aAttended - bAttended || a.name.localeCompare(b.name);
    });
  }

  ratingOf(memberId: string): number {
    return this.meal().ratings.find(r => r.familyMemberId === memberId)?.rating ?? 0;
  }

  rate(memberId: string, rating: number): void {
    const meal = this.meal();
    if (this.ratingOf(memberId) === rating) {
      // Tapping the current rating clears it
      this.plannedMealsService.deleteRating(meal.id, memberId).subscribe({
        next: () => this.meal.update(m => ({
          ...m,
          ratings: m.ratings.filter(r => r.familyMemberId !== memberId)
        })),
        error: () => this.showError()
      });
      return;
    }

    this.plannedMealsService.setRating(meal.id, memberId, rating).subscribe({
      next: updated => this.meal.set(updated),
      error: () => this.showError()
    });
  }

  close(): void {
    this.dialogRef.close(this.meal());
  }

  private showError(): void {
    this.snackBar.open('Could not save the rating', 'Dismiss', { duration: 4000 });
  }
}
