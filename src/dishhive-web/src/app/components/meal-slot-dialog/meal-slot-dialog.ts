import { ChangeDetectionStrategy, Component, Inject, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatSelectModule } from '@angular/material/select';
import { RecipesService } from '../../services/recipes.service';
import { StatisticsService } from '../../services/statistics.service';
import { DishStatistic } from '../../models/statistics.model';
import { RecipeListItem } from '../../models/recipe.model';
import { FamilyMember } from '../../models/family-member.model';
import { FrozenItem } from '../../models/frozen-item.model';
import {
  COURSE_LABELS,
  Course,
  CreatePlannedMeal,
  MEAL_TYPE_LABELS,
  MealType,
  PlannedMeal
} from '../../models/planned-meal.model';

export interface MealSlotDialogData {
  /** ISO date of the slot */
  date: string;
  /** Existing meal when editing, undefined when planning a new slot */
  meal?: PlannedMeal;
  members: FamilyMember[];
  freezerEnabled: boolean;
  freezerItems: FrozenItem[];
}

type PlanMode = 'recipe' | 'dish' | 'idea';

@Component({
  selector: 'app-meal-slot-dialog',
  standalone: true,
  imports: [
    FormsModule,
    RouterLink,
    MatDialogModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatSelectModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './meal-slot-dialog.html',
  styleUrl: './meal-slot-dialog.scss'
})
export class MealSlotDialog {
  readonly mode = signal<PlanMode>('dish');
  readonly selectedRecipe = signal<RecipeListItem | null>(null);
  readonly recipeResults = signal<RecipeListItem[]>([]);
  readonly attendeeIds = signal<Set<string>>(new Set());
  readonly freezyItemRef = signal<string | null>(null);
  readonly dishStatistics = signal<DishStatistic[]>([]);
  readonly currentDishName = signal('');

  /** Meal/course options for the compact "when & what course" selects */
  readonly mealTypeOptions = Object.entries(MEAL_TYPE_LABELS)
    .map(([value, label]) => ({ value: Number(value) as MealType, label }));
  readonly courseOptions = Object.entries(COURSE_LABELS)
    .map(([value, label]) => ({ value: Number(value) as Course, label }));

  mealType: MealType = MealType.Dinner;
  course: Course = Course.Main;
  dishName = '';
  vagueInstruction = '';
  notes = '';
  recipeSearch = '';

  /** Allergy and diet tags of the selected attendees, shown as a planning hint */
  readonly allergyHints = computed(() => {
    const selected = this.attendeeIds();
    return this.data.members
      .filter(m => selected.has(m.id) && (m.allergyTags.length > 0 || m.dietTags.length > 0))
      .map(m => `${m.name}: ${[...m.allergyTags, ...m.dietTags].join(', ')}`);
  });

  readonly selectedFreezerItem = computed(() =>
    this.data.freezerItems.find(i => i.id === this.freezyItemRef()) ?? null);

  /** History hint for the dish being planned ("you had this 2 weeks ago") */
  readonly lastPlannedHint = computed<DishStatistic | null>(() => {
    const name = (this.mode() === 'recipe'
      ? this.selectedRecipe()?.title
      : this.currentDishName())?.trim().toLowerCase();
    if (!name) {
      return null;
    }
    return this.dishStatistics().find(d => d.dishName.toLowerCase() === name) ?? null;
  });

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: MealSlotDialogData,
    private dialogRef: MatDialogRef<MealSlotDialog, CreatePlannedMeal>,
    private recipesService: RecipesService,
    statisticsService: StatisticsService
  ) {
    statisticsService.getDishStatistics().subscribe({
      next: stats => this.dishStatistics.set(stats.dishes),
      error: () => { /* the hint is a non-critical decoration */ }
    });

    const meal = data.meal;
    if (meal) {
      this.mode.set(meal.recipeId ? 'recipe' : meal.dishName ? 'dish' : 'idea');
      this.mealType = meal.mealType;
      this.course = meal.course;
      this.dishName = meal.dishName ?? '';
      this.currentDishName.set(this.dishName);
      this.vagueInstruction = meal.vagueInstruction ?? '';
      this.notes = meal.notes ?? '';
      this.attendeeIds.set(new Set(meal.attendeeIds));
      this.freezyItemRef.set(meal.freezyItemRef ?? null);
      if (meal.recipeId && meal.recipeTitle) {
        this.selectedRecipe.set({
          id: meal.recipeId,
          title: meal.recipeTitle,
          servings: 0,
          hasLocalImage: false,
          tags: []
        });
      }
    } else {
      // Household members eat at home by default; guests opt in per meal
      this.attendeeIds.set(new Set(data.members.filter(m => !m.isGuest).map(m => m.id)));
    }
  }

  searchRecipes(): void {
    const term = this.recipeSearch.trim();
    if (!term) {
      this.recipeResults.set([]);
      return;
    }
    this.recipesService.getRecipes(term).subscribe({
      next: recipes => this.recipeResults.set(recipes.slice(0, 5)),
      error: () => this.recipeResults.set([])
    });
  }

  selectRecipe(recipe: RecipeListItem): void {
    this.selectedRecipe.set(recipe);
    this.recipeResults.set([]);
    this.recipeSearch = '';
  }

  clearRecipe(): void {
    this.selectedRecipe.set(null);
  }

  toggleAttendee(memberId: string): void {
    this.attendeeIds.update(ids => {
      const next = new Set(ids);
      if (next.has(memberId)) {
        next.delete(memberId);
      } else {
        next.add(memberId);
      }
      return next;
    });
  }

  selectFreezerItem(item: FrozenItem): void {
    this.freezyItemRef.set(item.id);
    // A freezer meal is a decided dish; prefill the dish name from the item
    this.mode.set('dish');
    if (!this.dishName.trim()) {
      this.dishName = item.name;
    }
  }

  clearFreezerItem(): void {
    this.freezyItemRef.set(null);
  }

  isExpiringSoon(item: FrozenItem): boolean {
    if (!item.expirationDate) {
      return false;
    }
    const days = (new Date(item.expirationDate).getTime() - Date.now()) / 86_400_000;
    return days <= 7;
  }

  canSave(): boolean {
    switch (this.mode()) {
      case 'recipe': return this.selectedRecipe() !== null;
      case 'dish': return this.dishName.trim().length > 0;
      case 'idea': return this.vagueInstruction.trim().length > 0;
    }
  }

  save(): void {
    const mode = this.mode();
    const result: CreatePlannedMeal = {
      date: this.data.date,
      mealType: this.mealType,
      course: this.course,
      recipeId: mode === 'recipe' ? this.selectedRecipe()?.id : undefined,
      dishName: mode === 'dish' ? this.dishName.trim() : undefined,
      vagueInstruction: mode === 'idea' ? this.vagueInstruction.trim() : undefined,
      freezyItemRef: this.freezyItemRef() ?? undefined,
      notes: this.notes.trim() || undefined,
      familyMemberIds: [...this.attendeeIds()]
    };
    this.dialogRef.close(result);
  }
}
