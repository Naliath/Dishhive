import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MealRatingDialog, MealRatingDialogData } from '../../components/meal-rating-dialog/meal-rating-dialog';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { FamilyMember } from '../../models/family-member.model';
import { EatenStatus, PlannedMeal } from '../../models/planned-meal.model';

interface HistoryDay {
  date: string;
  meals: PlannedMeal[];
}

function toIso(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/**
 * Browsable history of past planned dishes. The planner's past rows are the history —
 * no separate event store (see docs/features/past-dishes-and-statistics.md).
 * This is also where meals are marked eaten/skipped and rated (meal-feedback.md).
 */
@Component({
  selector: 'app-history-page',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './history.page.html',
  styleUrl: './history.page.scss'
})
export class HistoryPage implements OnInit {
  /** Number of past days shown; grows with "Load more" */
  readonly daysBack = signal(28);
  readonly meals = signal<PlannedMeal[]>([]);
  readonly members = signal<FamilyMember[]>([]);
  readonly loading = signal(true);

  readonly EatenStatus = EatenStatus;

  readonly historyDays = computed<HistoryDay[]>(() => {
    const byDate = new Map<string, PlannedMeal[]>();
    for (const meal of this.meals()) {
      const group = byDate.get(meal.date) ?? [];
      group.push(meal);
      byDate.set(meal.date, group);
    }
    return [...byDate.entries()]
      .map(([date, meals]) => ({ date, meals }))
      .sort((a, b) => b.date.localeCompare(a.date));
  });

  constructor(
    private plannedMealsService: PlannedMealsService,
    private familyMembersService: FamilyMembersService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.load();
    this.familyMembersService.getMembers().subscribe({
      next: members => this.members.set(members),
      error: () => { /* rating dialog simply lists no members */ }
    });
  }

  load(): void {
    this.loading.set(true);
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const from = new Date(yesterday);
    from.setDate(from.getDate() - this.daysBack());

    this.plannedMealsService.getMeals(toIso(from), toIso(yesterday)).subscribe({
      next: meals => {
        this.meals.set(meals);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load the history', 'Dismiss', { duration: 4000 });
      }
    });
  }

  loadMore(): void {
    this.daysBack.update(days => days + 28);
    this.load();
  }

  mealSummary(meal: PlannedMeal): string {
    return meal.dishName ?? meal.vagueInstruction ?? '';
  }

  /** Cycle the eaten mark: unmarked → eaten → skipped → unmarked */
  cycleEaten(meal: PlannedMeal): void {
    const next = meal.eaten === EatenStatus.Eaten
      ? EatenStatus.Skipped
      : meal.eaten === EatenStatus.Skipped ? null : EatenStatus.Eaten;

    this.plannedMealsService.setEaten(meal.id, next).subscribe({
      next: updated => this.replaceMeal(updated),
      error: () => this.snackBar.open('Could not update the meal', 'Dismiss', { duration: 4000 })
    });
  }

  eatenIcon(meal: PlannedMeal): string {
    switch (meal.eaten) {
      case EatenStatus.Eaten: return 'check_circle';
      case EatenStatus.Skipped: return 'cancel';
      default: return 'radio_button_unchecked';
    }
  }

  eatenTooltip(meal: PlannedMeal): string {
    switch (meal.eaten) {
      case EatenStatus.Eaten: return 'Eaten — tap to mark skipped';
      case EatenStatus.Skipped: return 'Skipped — tap to clear';
      default: return 'Tap to mark as eaten';
    }
  }

  averageRating(meal: PlannedMeal): string | null {
    if (meal.ratings.length === 0) {
      return null;
    }
    const avg = meal.ratings.reduce((sum, r) => sum + r.rating, 0) / meal.ratings.length;
    return avg.toFixed(1);
  }

  openRating(meal: PlannedMeal): void {
    const data: MealRatingDialogData = { meal, members: this.members() };
    this.dialog.open(MealRatingDialog, { data })
      .afterClosed()
      .subscribe((updated?: PlannedMeal) => {
        if (updated) {
          this.replaceMeal(updated);
        }
      });
  }

  private replaceMeal(updated: PlannedMeal): void {
    this.meals.update(meals => meals.map(m => m.id === updated.id ? updated : m));
  }
}
