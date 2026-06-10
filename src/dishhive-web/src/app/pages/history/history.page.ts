import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { PlannedMeal } from '../../models/planned-meal.model';

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
 */
@Component({
  selector: 'app-history-page',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './history.page.html',
  styleUrl: './history.page.scss'
})
export class HistoryPage implements OnInit {
  /** Number of past days shown; grows with "Load more" */
  readonly daysBack = signal(28);
  readonly meals = signal<PlannedMeal[]>([]);
  readonly loading = signal(true);

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
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.load();
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
}
