import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { WeekPlannerService } from '../../services/week-planner.service';
import { RecipesService } from '../../services/recipes.service';
import { SettingsService, toLocalDateString } from '../../services/settings.service';
import { WeekPlan, DayOfWeek, PlannedMeal } from '../../models/week-plan.model';
import { RecipeSummary } from '../../models/recipe.model';
import { PlanMealDialogComponent, PlanMealDialogData } from '../../components/plan-meal-dialog/plan-meal-dialog.component';

const DAY_NAMES: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const DAY_ISO_MAP: Record<DayOfWeek, number> = {
  Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6, Sunday: 0
};

@Component({
  selector: 'app-week-planner',
  standalone: true,
  imports: [
    CommonModule,
    MatProgressSpinnerModule,
    MatIconModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatSnackBarModule,
    MatTooltipModule,
    RouterLink,
  ],
  templateUrl: './week-planner.page.html',
  styleUrl: './week-planner.page.scss'
})
export class WeekPlannerPage implements OnInit {
  weekPlan = signal<WeekPlan | null>(null);
  loading = signal(true);
  currentWeekStart = signal<string>('');
  recipes = signal<RecipeSummary[]>([]);

  /** Ordered list of days based on configured week start day. */
  readonly orderedDays = computed<DayOfWeek[]>(() => {
    const startDay = this.settings.weekStartDay();
    const startIdx = DAY_NAMES.indexOf(startDay);
    return [
      ...DAY_NAMES.slice(startIdx),
      ...DAY_NAMES.slice(0, startIdx)
    ];
  });

  constructor(
    private weekPlannerService: WeekPlannerService,
    private recipesService: RecipesService,
    private settings: SettingsService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    // Load settings first so week-start day is correct before computing weekStart
    this.settings.loadAll().subscribe({
      complete: () => this.init(),
      error: () => this.init()
    });
    this.recipesService.getAll().subscribe(r => this.recipes.set(r));
  }

  private init(): void {
    const weekStart = this.settings.getWeekStart();
    this.currentWeekStart.set(weekStart);
    this.loadOrCreateWeekPlan(weekStart);
  }

  loadOrCreateWeekPlan(weekStart: string): void {
    this.loading.set(true);
    this.weekPlannerService.getOrCreate(weekStart).subscribe({
      next: (plan) => { this.weekPlan.set(plan); this.loading.set(false); },
      error: () => { this.weekPlan.set(null); this.loading.set(false); }
    });
  }

  getMealForDay(day: DayOfWeek): PlannedMeal | undefined {
    return this.weekPlan()?.meals.find(m => m.dayOfWeek === day && m.mealType === 'Dinner');
  }

  /** Returns the calendar date for a given day in the current week. */
  getDateForDay(day: DayOfWeek): Date {
    const weekStart = new Date(this.currentWeekStart() + 'T00:00:00');
    // currentWeekStart is always the configured start day; offset from there
    const startDayName = this.settings.weekStartDay();
    const startDayOffset = DAY_NAMES.indexOf(startDayName);
    const targetOffset = DAY_NAMES.indexOf(day);
    let diff = targetOffset - startDayOffset;
    if (diff < 0) diff += 7;
    const d = new Date(weekStart);
    d.setDate(d.getDate() + diff);
    return d;
  }

  openPlanMealDialog(day: DayOfWeek): void {
    const existing = this.getMealForDay(day);
    const dialogRef = this.dialog.open<PlanMealDialogComponent, PlanMealDialogData>(
      PlanMealDialogComponent,
      {
        data: { day, existingMeal: existing, recipes: this.recipes() },
        width: '420px'
      }
    );

    dialogRef.afterClosed().subscribe(result => {
      if (result === null || result === undefined) return;

      const plan = this.weekPlan();
      if (!plan) return;

      if (result === 'clear') {
        if (!existing) return;
        this.weekPlannerService.deleteMeal(plan.id, existing.id).subscribe({
          next: () => {
            this.weekPlan.update(p => p ? {
              ...p,
              meals: p.meals.filter(m => m.id !== existing.id)
            } : p);
            this.snackBar.open('Meal removed', undefined, { duration: 2000 });
          },
          error: () => this.snackBar.open('Failed to remove meal', 'Dismiss', { duration: 3000 })
        });
        return;
      }

      this.weekPlannerService.upsertMeal(plan.id, {
        dayOfWeek: day,
        mealType: 'Dinner',
        recipeId: result.recipeId,
        vagueInstruction: result.vagueInstruction,
        notes: result.notes,
        isFromFreezer: result.isFromFreezer,
      }).subscribe({
        next: (saved) => {
          this.weekPlan.update(p => {
            if (!p) return p;
            const meals = p.meals.filter(m => !(m.dayOfWeek === day && m.mealType === 'Dinner'));
            return { ...p, meals: [...meals, saved as unknown as PlannedMeal] };
          });
          this.snackBar.open('Meal saved', undefined, { duration: 2000 });
        },
        error: () => this.snackBar.open('Failed to save meal', 'Dismiss', { duration: 3000 })
      });
    });
  }

  previousWeek(): void {
    const current = new Date(this.currentWeekStart() + 'T00:00:00');
    current.setDate(current.getDate() - 7);
    const newStart = toLocalDateString(current);
    this.currentWeekStart.set(newStart);
    this.loadOrCreateWeekPlan(newStart);
  }

  nextWeek(): void {
    const current = new Date(this.currentWeekStart() + 'T00:00:00');
    current.setDate(current.getDate() + 7);
    const newStart = toLocalDateString(current);
    this.currentWeekStart.set(newStart);
    this.loadOrCreateWeekPlan(newStart);
  }

  isToday(day: DayOfWeek): boolean {
    const date = this.getDateForDay(day);
    const today = new Date();
    return date.getFullYear() === today.getFullYear() &&
           date.getMonth() === today.getMonth() &&
           date.getDate() === today.getDate();
  }

  formatWeekRange(): string {
    const days = this.orderedDays();
    const first = this.getDateForDay(days[0]);
    const last = this.getDateForDay(days[days.length - 1]);
    const opts: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short' };
    return `${first.toLocaleDateString('en-GB', opts)} – ${last.toLocaleDateString('en-GB', { ...opts, year: 'numeric' })}`;
  }
}
