import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { FreezerService } from '../../services/freezer.service';
import { MealSuggestionsService } from '../../services/meal-suggestions.service';
import { RecipesService } from '../../services/recipes.service';
import { MealSuggestion } from '../../models/meal-suggestion.model';
import {
  COURSE_LABELS,
  COURSE_ORDER,
  Course,
  CreatePlannedMeal,
  EatenStatus,
  MEAL_TYPE_LABELS,
  MealType,
  PlannedMeal
} from '../../models/planned-meal.model';
import { FamilyMember } from '../../models/family-member.model';
import { FreezerSuggestions } from '../../models/frozen-item.model';
import { MealSlotDialog, MealSlotDialogData } from '../../components/meal-slot-dialog/meal-slot-dialog';
import {
  SuggestionReviewDialog,
  SuggestionReviewDialogData
} from '../../components/suggestion-review-dialog/suggestion-review-dialog';

interface PlannerDay {
  date: Date;
  iso: string;
  isToday: boolean;
  /** Day has passed: eaten feedback becomes available */
  isPast: boolean;
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
    MatSnackBarModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './week-planner.page.html',
  styleUrl: './week-planner.page.scss'
})
export class WeekPlannerPage implements OnInit {
  /** Below this, a load doesn't show the spinner at all — avoids a flash for fast responses */
  private static readonly LOADING_INDICATOR_DELAY_MS = 100;

  private readonly destroyRef = inject(DestroyRef);
  private loadingTimer: ReturnType<typeof setTimeout> | undefined;

  readonly weekStart = signal<Date>(mondayOf(new Date()));
  readonly meals = signal<PlannedMeal[]>([]);
  readonly members = signal<FamilyMember[]>([]);
  readonly freezer = signal<FreezerSuggestions>({ enabled: false, items: [] });
  readonly suggestionsEnabled = signal(false);
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
      return { date, iso, isToday: iso === todayIso, isPast: iso < todayIso, meals };
    });
  });

  constructor(
    private plannedMealsService: PlannedMealsService,
    private familyMembersService: FamilyMembersService,
    private freezerService: FreezerService,
    private mealSuggestionsService: MealSuggestionsService,
    private recipesService: RecipesService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {
    this.destroyRef.onDestroy(() => this.clearLoadingTimer());
  }

  ngOnInit(): void {
    forkJoin({
      members: this.familyMembersService.getMembers(),
      freezer: this.freezerService.getSuggestions(),
      suggestions: this.mealSuggestionsService.getStatus()
    }).subscribe({
      next: ({ members, freezer, suggestions }) => {
        this.members.set(members);
        this.freezer.set(freezer);
        this.suggestionsEnabled.set(suggestions.enabled);
      },
      error: () => this.snackBar.open('Could not load planner data', 'Dismiss', { duration: 4000 })
    });
    this.loadWeek();
  }

  loadWeek(): void {
    this.beginLoading();
    this.plannedMealsService.getMeals(toIso(this.weekStart()), toIso(this.weekEnd())).subscribe({
      next: meals => {
        this.meals.set(meals);
        this.endLoading();
      },
      error: () => {
        this.endLoading();
        this.snackBar.open('Could not load the week plan', 'Dismiss', { duration: 4000 });
      }
    });
  }

  /**
   * Starts a load without flashing the spinner for a fast response: `loading` only
   * flips true if the request is still in flight after LOADING_INDICATOR_DELAY_MS.
   * A slow request still gets the spinner, so the page never looks stuck.
   */
  private beginLoading(): void {
    this.clearLoadingTimer();
    this.loadingTimer = setTimeout(
      () => this.loading.set(true), WeekPlannerPage.LOADING_INDICATOR_DELAY_MS);
  }

  private endLoading(): void {
    this.clearLoadingTimer();
    this.loading.set(false);
  }

  private clearLoadingTimer(): void {
    if (this.loadingTimer !== undefined) {
      clearTimeout(this.loadingTimer);
      this.loadingTimer = undefined;
    }
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
      freezerItems: this.freezer().items,
      plannedRecipeIds: this.meals()
        .map(m => m.recipeId)
        .filter((id): id is string => !!id)
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
          // Update the affected slot in place instead of reloading the whole week —
          // a full loadWeek() would flash the grid to a spinner and back for a
          // single-slot change
          next: saved => this.meals.update(meals => meal
            ? meals.map(m => m.id === saved.id ? saved : m)
            : [...meals, saved]),
          error: (error: HttpErrorResponse) => {
            const detail = error.error?.title ?? 'Could not save the meal';
            this.snackBar.open(detail, 'Dismiss', { duration: 4000 });
          }
        });
      });
  }

  /** Opens the AI suggestion review dialog; accepted proposals become planned meals */
  suggestWeek(): void {
    const data: SuggestionReviewDialogData = { weekStart: toIso(this.weekStart()) };
    this.dialog.open<SuggestionReviewDialog, SuggestionReviewDialogData, MealSuggestion[]>(
      SuggestionReviewDialog, { data })
      .afterClosed().subscribe(selected => {
        if (!selected || selected.length === 0) {
          return;
        }

        const householdIds = this.members().filter(m => !m.isGuest).map(m => m.id);

        // A planned day replaces its "idea": once a suggested dish lands on a date
        // that only held a vague-instruction dinner main, that idea meal is removed
        // (it was input for the AI; it shouldn't linger next to the concrete dish).
        const plannedDates = new Set(selected.map(s => s.date));
        const ideasToRemove = this.meals().filter(m =>
          plannedDates.has(m.date)
          && m.mealType === MealType.Dinner
          && m.course === Course.Main
          && !!m.vagueInstruction
          && !m.dishName
          && !m.recipeId);

        const removals = ideasToRemove.length > 0
          ? forkJoin(ideasToRemove.map(m => this.plannedMealsService.deleteMeal(m.id)))
          : of([]);

        // Each accepted suggestion becomes a planned meal. An external suggestion
        // (found online, no recipe yet) is imported first, then planned by recipe id.
        // Per-suggestion catchError keeps a single failed import from dropping the rest.
        const creations = selected.map(suggestion => {
          const plan = (recipeId?: string, dishName?: string) => this.plannedMealsService.createMeal({
            date: suggestion.date,
            mealType: MealType.Dinner,
            course: Course.Main,
            recipeId: recipeId ?? suggestion.recipeId,
            dishName: dishName ?? suggestion.dishName,
            // Carry the freezer reservation through so accepted freezer dishes draw down stock
            freezyItemRef: suggestion.freezyItemRef,
            freezyItemQuantity: suggestion.freezyItemQuantity,
            familyMemberIds: householdIds
          });

          const operation = suggestion.sourceUrl && !suggestion.recipeId
            ? this.recipesService.importRecipe(suggestion.sourceUrl).pipe(
                switchMap(recipe => plan(recipe.id, recipe.title)))
            : plan();

          return operation.pipe(
            map(() => ({ ok: true })),
            catchError(() => of({ ok: false, dish: suggestion.dishName }))
          );
        });

        removals.pipe(switchMap(() => forkJoin(creations))).subscribe({
          next: results => {
            this.loadWeek();
            const added = results.filter(r => r.ok).length;
            const failed = results.length - added;
            const message = failed === 0
              ? `Added ${added} suggested dinner${added === 1 ? '' : 's'}`
              : `Added ${added}; ${failed} could not be imported`;
            this.snackBar.open(message, 'Dismiss', { duration: failed === 0 ? 3000 : 5000 });
          },
          error: () => {
            this.loadWeek();
            this.snackBar.open('Could not add all suggestions', 'Dismiss', { duration: 4000 });
          }
        });
      });
  }

  clearSlot(meal: PlannedMeal): void {
    this.plannedMealsService.deleteMeal(meal.id).subscribe({
      // Remove the slot locally instead of reloading the whole week — a full
      // loadWeek() would flash the grid to a spinner and back for one removed dish
      next: () => this.meals.update(meals => meals.filter(m => m.id !== meal.id)),
      error: () => this.snackBar.open('Could not clear the slot', 'Dismiss', { duration: 4000 })
    });
  }

  mealSummary(meal: PlannedMeal): string {
    return meal.dishName ?? meal.vagueInstruction ?? '';
  }

  isEaten(meal: PlannedMeal): boolean {
    return meal.eaten === EatenStatus.Eaten;
  }

  /** One-tap eaten toggle on past days; skipping and rating live on the history page */
  toggleEaten(meal: PlannedMeal): void {
    const next = this.isEaten(meal) ? null : EatenStatus.Eaten;
    this.plannedMealsService.setEaten(meal.id, next).subscribe({
      next: updated => this.meals.update(meals => meals.map(m => m.id === updated.id ? updated : m)),
      error: () => this.snackBar.open('Could not update the meal', 'Dismiss', { duration: 4000 })
    });
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
