import { ChangeDetectionStrategy, Component, Inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { FamilyMember } from '../../models/family-member.model';
import { Recipe } from '../../models/recipe.model';
import {
  COURSE_LABELS,
  COURSE_ORDER,
  Course,
  CreatePlannedMeal,
  MEAL_TYPE_LABELS,
  MealType,
  PlannedMeal
} from '../../models/planned-meal.model';
import { LocalizedDatePipe } from '../../services/language.service';

export interface QuickPlanDialogData {
  recipe: Pick<Recipe, 'id' | 'title'>;
  startDate: string;
  meals: PlannedMeal[];
  members: FamilyMember[];
}

export interface QuickPlanDay {
  date: Date;
  iso: string;
  isToday: boolean;
  meals: PlannedMeal[];
}

function fromIso(iso: string): Date {
  const [year, month, day] = iso.split('-').map(Number);
  return new Date(year, month - 1, day);
}

function toIso(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

export function buildQuickPlanDays(
  startDate: string,
  meals: PlannedMeal[],
  today = toIso(new Date())
): QuickPlanDay[] {
  const mealsByDate = new Map<string, PlannedMeal[]>();
  for (const meal of meals) {
    const dateMeals = mealsByDate.get(meal.date) ?? [];
    dateMeals.push(meal);
    mealsByDate.set(meal.date, dateMeals);
  }

  return Array.from({ length: 7 }, (_, index) => {
    const date = fromIso(startDate);
    date.setDate(date.getDate() + index);
    const iso = toIso(date);
    const dayMeals = [...(mealsByDate.get(iso) ?? [])]
      .sort((a, b) => a.mealType - b.mealType || COURSE_ORDER[a.course] - COURSE_ORDER[b.course]);
    return { date, iso, isToday: iso === today, meals: dayMeals };
  });
}

@Component({
  selector: 'app-quick-plan-dialog',
  standalone: true,
  imports: [
    LocalizedDatePipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatIconModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './quick-plan-dialog.html',
  styleUrl: './quick-plan-dialog.scss'
})
export class QuickPlanDialog {
  readonly Course = Course;
  readonly course = signal(Course.Main);
  readonly days: QuickPlanDay[];

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: QuickPlanDialogData,
    private dialogRef: MatDialogRef<QuickPlanDialog, CreatePlannedMeal>
  ) {
    this.days = buildQuickPlanDays(data.startDate, data.meals);
  }

  mealLabel(meal: PlannedMeal): string {
    return `${MEAL_TYPE_LABELS[meal.mealType]} · ${COURSE_LABELS[meal.course]}`;
  }

  mealSummary(meal: PlannedMeal): string {
    return meal.recipeTitle ?? meal.dishName ?? meal.vagueInstruction ?? 'Planned meal';
  }

  plan(day: QuickPlanDay): void {
    this.dialogRef.close({
      date: day.iso,
      mealType: MealType.Dinner,
      course: this.course(),
      recipeId: this.data.recipe.id,
      familyMemberIds: this.data.members.filter(member => !member.isGuest).map(member => member.id)
    });
  }
}
