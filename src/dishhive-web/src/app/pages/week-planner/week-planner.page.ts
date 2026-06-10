import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { FreezerService } from '../../services/freezer.service';
import {
  COURSE_LABELS,
  COURSE_ORDER,
  Course,
  CreatePlannedMeal,
  MEAL_TYPE_LABELS,
  MealType,
  PlannedMeal
} from '../../models/planned-meal.model';
import { FamilyMember } from '../../models/family-member.model';
import { FreezerSuggestions } from '../../models/frozen-item.model';
import { MealSlotDialog, MealSlotDialogData } from '../../components/meal-slot-dialog/meal-slot-dialog';

interface PlannerDay {
  date: Date;
  iso: string;
  isToday: boolean;
  /** All dishes planned for the day, in meal then serving order */
  meals: PlannedMeal[];
}

/** ISO date (yyyy-MM-dd) from local date components, avoiding UTC shifts */
function toIso(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

function mondayOf(date: Date): Date {
  const monday = new Date(date);
  monday.setHours(0, 0, 0, 0);
  monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
  return monday;
}

@Component({
  selector: 'app-week-planner-page',
  standalone: true,
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './week-planner.page.html',
  styleUrl: './week-planner.page.scss'
})
export class WeekPlannerPage implements OnInit {
  readonly weekStart = signal<Date>(mondayOf(new Date()));
  readonly meals = signal<PlannedMeal[]>([]);
  readonly members = signal<FamilyMember[]>([]);
  readonly freezer = signal<FreezerSuggestions>({ enabled: false, items: [] });
  readonly loading = signal(true);

  readonly weekEnd = computed(() => {
    const end = new Date(this.weekStart());
    end.setDate(end.getDate() + 6);
    return end;
  });

  /** Query params for the shopping list of the viewed week */
  readonly shoppingListParams = computed(() => ({
    from: toIso(this.weekStart()),
    to: toIso(this.weekEnd())
  }));

  readonly days = computed<PlannerDay[]>(() => {
    const todayIso = toIso(new Date());
    const mealsByDate = new Map<string, PlannedMeal[]>();
    for (const meal of this.meals()) {
      const list = mealsByDate.get(meal.date) ?? [];
      list.push(meal);
      mealsByDate.set(meal.date, list);
    }
    return Array.from({ length: 7 }, (_, i) => {
      const date = new Date(this.weekStart());
      date.setDate(date.getDate() + i);
      const iso = toIso(date);
      const meals = (mealsByDate.get(iso) ?? [])
        .sort((a, b) => a.mealType - b.mealType || COURSE_ORDER[a.course] - COURSE_ORDER[b.course]);
      return { date, iso, isToday: iso === todayIso, meals };
    });
  });

  constructor(
    private plannedMealsService: PlannedMealsService,
    private familyMembersService: FamilyMembersService,
    private freezerService: FreezerService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    forkJoin({
      members: this.familyMembersService.getMembers(),
      freezer: this.freezerService.getSuggestions()
    }).subscribe({
      next: ({ members, freezer }) => {
        this.members.set(members);
        this.freezer.set(freezer);
      },
      error: () => this.snackBar.open('Could not load planner data', 'Dismiss', { duration: 4000 })
    });
    this.loadWeek();
  }

  loadWeek(): void {
    this.loading.set(true);
    this.plannedMealsService.getMeals(toIso(this.weekStart()), toIso(this.weekEnd())).subscribe({
      next: meals => {
        this.meals.set(meals);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load the week plan', 'Dismiss', { duration: 4000 });
      }
    });
  }

  previousWeek(): void {
    this.shiftWeek(-7);
  }

  nextWeek(): void {
    this.shiftWeek(7);
  }

  goToCurrentWeek(): void {
    this.weekStart.set(mondayOf(new Date()));
    this.loadWeek();
  }

  private shiftWeek(days: number): void {
    const start = new Date(this.weekStart());
    start.setDate(start.getDate() + days);
    this.weekStart.set(start);
    this.loadWeek();
  }

  openSlot(day: PlannerDay, meal?: PlannedMeal): void {
    const data: MealSlotDialogData = {
      date: day.iso,
      meal,
      members: this.members(),
      freezerEnabled: this.freezer().enabled,
      freezerItems: this.freezer().items
    };

    this.dialog.open<MealSlotDialog, MealSlotDialogData, CreatePlannedMeal>(MealSlotDialog, { data })
      .afterClosed().subscribe(result => {
        if (!result) {
          return;
        }
        const request = meal
          ? this.plannedMealsService.updateMeal(meal.id, result)
          : this.plannedMealsService.createMeal(result);
        request.subscribe({
          next: () => this.loadWeek(),
          error: (error: HttpErrorResponse) => {
            const detail = error.error?.title ?? 'Could not save the meal';
            this.snackBar.open(detail, 'Dismiss', { duration: 4000 });
          }
        });
      });
  }

  clearSlot(meal: PlannedMeal): void {
    this.plannedMealsService.deleteMeal(meal.id).subscribe({
      next: () => this.loadWeek(),
      error: () => this.snackBar.open('Could not clear the slot', 'Dismiss', { duration: 4000 })
    });
  }

  mealSummary(meal: PlannedMeal): string {
    return meal.dishName ?? meal.vagueInstruction ?? '';
  }

  /**
   * Label shown only when a dish deviates from the everyday case
   * (dinner main course), e.g. "Lunch", "Dessert" or "Lunch · Dessert"
   */
  mealLabel(meal: PlannedMeal): string | null {
    const parts: string[] = [];
    if (meal.mealType !== MealType.Dinner) {
      parts.push(MEAL_TYPE_LABELS[meal.mealType]);
    }
    if (meal.course !== Course.Main) {
      parts.push(COURSE_LABELS[meal.course]);
    }
    return parts.length > 0 ? parts.join(' · ') : null;
  }

  attendeeNames(meal: PlannedMeal): string {
    const byId = new Map(this.members().map(m => [m.id, m.name]));
    return meal.attendeeIds
      .map(id => byId.get(id))
      .filter((name): name is string => !!name)
      .join(', ');
  }
}
