import { LanguageService as UiLanguageService } from '../../services/language.service';
import { HouseholdClockService } from '../../services/household-clock.service';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { LocalizedDatePipe, TranslatePipe } from '../../services/language.service';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { FreezerService } from '../../services/freezer.service';
import { MealSuggestionsService } from '../../services/meal-suggestions.service';
import { SettingsService } from '../../services/settings.service';
import { SuggestionReviewResult } from '../../models/meal-suggestion.model';
import {
  COURSE_ORDER,
  Course,
  CreatePlannedMeal,
  EatenStatus,
  MealType,
  PlannedMeal
} from '../../models/planned-meal.model';
import { FamilyMember } from '../../models/family-member.model';
import { FreezerSuggestions } from '../../models/frozen-item.model';
import { FreezerReconciliationDto } from '../../api/generated/dishhive-api.client';
import { FreezerReservationsDialog, FreezerReservationsResult } from '../../components/freezer-reservations-dialog/freezer-reservations-dialog';
import { CookingLoaderComponent } from '../../components/cooking-loader/cooking-loader';
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

@Component({
  selector: 'app-week-planner-page',
  standalone: true,
  imports: [
    CookingLoaderComponent,
    LocalizedDatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatSnackBarModule,
    MatTooltipModule,
    TranslatePipe
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './week-planner.page.html',
  styleUrl: './week-planner.page.scss'
})
export class WeekPlannerPage implements OnInit {
  readonly uiLanguage = inject(UiLanguageService);
  readonly clock = inject(HouseholdClockService);
  /** Below this, a load doesn't show the spinner at all — avoids a flash for fast responses */
  private static readonly LOADING_INDICATOR_DELAY_MS = 100;

  private readonly destroyRef = inject(DestroyRef);
  private loadingTimer: ReturnType<typeof setTimeout> | undefined;
  private weekLoadSequence = 0;

  readonly weekStart = signal<Date>(this.clock.date());
  readonly meals = signal<PlannedMeal[]>([]);
  readonly members = signal<FamilyMember[]>([]);
  readonly freezer = signal<FreezerSuggestions>({ enabled: false, items: [] });
  readonly freezerReservations = signal<FreezerReconciliationDto | null>(null);
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
    const todayIso = this.clock.today();
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
    private settingsService: SettingsService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {
    this.weekStart.set(this.settingsService.startOfWeek(this.clock.date()));
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
        if (freezer.enabled) this.refreshFreezerReservations();
        this.suggestionsEnabled.set(suggestions.enabled);
      },
      error: () => this.snackBar.open(this.uiLanguage.t('ui.couldNotLoadPlannerData'), this.uiLanguage.t('common.dismiss'), { duration: 4000 })
    });
    this.loadWeek();
  }

  loadWeek(): void {
    if (this.freezer().enabled) this.refreshFreezerReservations();
    const sequence = ++this.weekLoadSequence;
    this.beginLoading();
    this.plannedMealsService.getMeals(toIso(this.weekStart()), toIso(this.weekEnd())).subscribe({
      next: meals => {
        if (sequence !== this.weekLoadSequence) return;
        this.meals.set(meals);
        this.endLoading();
      },
      error: () => {
        if (sequence !== this.weekLoadSequence) return;
        this.endLoading();
        this.snackBar.open(this.uiLanguage.t('ui.couldNotLoadTheWeekPlan'), this.uiLanguage.t('common.dismiss'), { duration: 4000 });
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
    this.weekStart.set(this.settingsService.startOfWeek(this.clock.date()));
    this.loadWeek();
  }

  private shiftWeek(days: number): void {
    const start = new Date(this.weekStart());
    start.setDate(start.getDate() + days);
    this.weekStart.set(start);
    this.loadWeek();
  }

  private refreshFreezerReservations(): void {
    this.freezerService.getReservations().subscribe({
      next: result => this.freezerReservations.set(result),
      error: () => this.freezerReservations.set({ state: 'unavailable', checkedAt: new Date().toISOString(), issues: [] })
    });
  }

  reviewFreezerReservations(): void {
    this.dialog.open<FreezerReservationsDialog, void, FreezerReservationsResult>(FreezerReservationsDialog, {
      width: '700px', maxWidth: 'calc(100vw - 32px)'
    }).afterClosed().subscribe(result => {
      this.refreshFreezerReservations();
      this.freezerService.getSuggestions().subscribe(stock => this.freezer.set(stock));
      this.loadWeek();
      if (result?.editMealId) this.plannedMealsService.getMeal(result.editMealId).subscribe({
        next: meal => this.openSlot({ iso: meal.date }, meal),
        error: () => this.snackBar.open(this.uiLanguage.t('ui.couldNotLoadTheMeal'), this.uiLanguage.t('common.dismiss'), { duration: 4000 })
      });
    });
  }

  openSlot(day: Pick<PlannerDay, 'iso'>, meal?: PlannedMeal): void {
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
          next: saved => {
            this.meals.update(meals => meal ? meals.map(m => m.id === saved.id ? saved : m) : [...meals, saved]);
            if (this.freezer().enabled) this.refreshFreezerReservations();
          },
          error: (error: HttpErrorResponse) => {
            const detail = error.error?.title ?? this.uiLanguage.t('aiStats.reviewOutcomes.planningFailed');
            this.snackBar.open(detail, this.uiLanguage.t('common.dismiss'), { duration: 4000 });
          }
        });
      });
  }

  /** Opens the AI suggestion review dialog; accepted proposals become planned meals */
  suggestWeek(): void {
    const data: SuggestionReviewDialogData = {
      weekStart: toIso(this.weekStart()),
      days: this.days().map(day => ({
        date: day.iso,
        hasPlannedDinner: day.meals.some(meal =>
          meal.mealType === MealType.Dinner
          && meal.course === Course.Main
          && (!!meal.recipeId || !!meal.dishName))
      }))
    };
    this.dialog.open<SuggestionReviewDialog, SuggestionReviewDialogData, SuggestionReviewResult>(
      SuggestionReviewDialog, {
        data,
        panelClass: 'suggestion-review-dialog-panel',
        maxWidth: '100vw'
      })
      .afterClosed().subscribe(selection => {
        this.loadWeek();
        if (selection?.applied) {
          return;
        }
        if (!selection || selection.suggestions.length === 0) {
          return;
        }

        this.mealSuggestionsService
          .acceptSuggestions(selection.batchId, selection.suggestions, selection.rejectedSuggestionIds)
          .subscribe({
          next: response => {
            this.loadWeek();
            const added = response.results.filter(result =>
              result.status === 'created' || result.status === 'alreadyApplied').length;
            const failed = response.results.filter(result =>
              result.status !== 'created' && result.status !== 'alreadyApplied');
            const failedNames = failed.slice(0, 2).map(result => result.dishName).join(', ');
            const message = failed.length === 0
              ? this.uiLanguage.plural('weekPlanner.addedSuggestions', added)
              : this.uiLanguage.t('weekPlanner.partialSuggestions', {
                  count: added, names: failedNames,
                  more: failed.length > 2 ? this.uiLanguage.t('weekPlanner.moreFailures', { count: failed.length - 2 }) : ''
                });
            this.snackBar.open(message, this.uiLanguage.t('common.dismiss'), { duration: failed.length === 0 ? 3000 : 5000 });
          },
          error: () => {
            this.loadWeek();
            this.snackBar.open(this.uiLanguage.t('ui.couldNotApplyTheSelectedSuggestions'), this.uiLanguage.t('common.dismiss'), { duration: 4000 });
          }
        });
      });
  }

  clearSlot(meal: PlannedMeal): void {
    this.plannedMealsService.deleteMeal(meal.id).subscribe({
      // Remove the slot locally instead of reloading the whole week — a full
      // loadWeek() would flash the grid to a spinner and back for one removed dish
      next: () => {
        this.meals.update(meals => meals.filter(m => m.id !== meal.id));
        if (this.freezer().enabled) this.refreshFreezerReservations();
      },
      error: () => this.snackBar.open(this.uiLanguage.t('ui.couldNotClearTheSlot'), this.uiLanguage.t('common.dismiss'), { duration: 4000 })
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
      error: () => this.snackBar.open(this.uiLanguage.t('ui.couldNotUpdateTheMeal'), this.uiLanguage.t('common.dismiss'), { duration: 4000 })
    });
  }

  /**
   * Label shown only when a dish deviates from the everyday case
   * (dinner main course), e.g. "Lunch", "Dessert" or "Lunch · Dessert"
   */
  mealLabel(meal: PlannedMeal): string | null {
    const parts: string[] = [];
    if (meal.mealType !== MealType.Dinner) {
      parts.push(this.uiLanguage.mealTypeLabel(meal.mealType));
    }
    if (meal.course !== Course.Main) {
      parts.push(this.uiLanguage.courseLabel(meal.course));
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
