import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable, forkJoin } from 'rxjs';
import { RecipesService } from '../../services/recipes.service';
import { CookbooksService } from '../../services/cookbooks.service';
import { MeasurementService } from '../../services/measurement.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { StatisticsService } from '../../services/statistics.service';
import { ClassPickerComponent } from '../../components/class-picker/class-picker';
import { CookingLoaderComponent } from '../../components/cooking-loader/cooking-loader';
import { MealRatingDialog, MealRatingDialogData } from '../../components/meal-rating-dialog/meal-rating-dialog';
import {
  QuickPlanDialog,
  QuickPlanDialogData
} from '../../components/quick-plan-dialog/quick-plan-dialog';
import { Cookbook, DietaryFactsStatus, Recipe } from '../../models/recipe.model';
import { CreatePlannedMeal } from '../../models/planned-meal.model';
import { ingredientClassLabel } from '../../models/ingredient-class.model';
import { DishStatistic } from '../../models/statistics.model';
import { FamilyMember, FamilyMemberFavorite } from '../../models/family-member.model';

/** ISO date (yyyy-MM-dd) from local date components, avoiding UTC shifts */
function toIso(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

@Component({
  selector: 'app-recipe-detail-page',
  standalone: true,
  imports: [
    ClassPickerComponent,
    CookingLoaderComponent,
    DatePipe,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatListModule,
    MatMenuModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipe-detail.page.html',
  styleUrl: './recipe-detail.page.scss'
})
export class RecipeDetailPage implements OnInit {
  readonly recipe = signal<Recipe | null>(null);
  readonly loading = signal(true);
  readonly quickPlanLoading = signal(false);
  /** Temporary serving count used to scale ingredient quantities on this page. */
  readonly selectedServings = signal(1);
  /** Show the verbatim source lines next to normalized values (imported recipes) */
  readonly showOriginal = signal(false);

  readonly members = signal<FamilyMember[]>([]);
  readonly favoritesByMember = signal<Map<string, FamilyMemberFavorite[]>>(new Map());
  readonly dishStatistics = signal<DishStatistic[]>([]);

  /** Planning history of this dish (matched by title), for the "last planned" widget */
  readonly stats = computed<DishStatistic | null>(() => {
    const title = this.recipe()?.title.trim().toLowerCase();
    if (!title) {
      return null;
    }
    return this.dishStatistics().find(d => d.dishName.toLowerCase() === title) ?? null;
  });

  /** Members that marked this recipe as a favorite — only these show as chips */
  readonly favoritedMembers = computed(() =>
    this.members().filter(m => this.favoriteEntry(m.id) !== null));

  /** Members offered in the "mark as favorite" menu */
  readonly otherMembers = computed(() =>
    this.members().filter(m => this.favoriteEntry(m.id) === null));

  /** Manual collections, split by this recipe's membership */
  private readonly manualCookbooks = computed(() =>
    this.cookbooks().filter(c => c.kind === 'manual'));
  readonly memberOfCookbooks = computed(() =>
    this.manualCookbooks().filter(c => this.recipe()?.cookbookIds.includes(c.id)));
  readonly availableCookbooks = computed(() =>
    this.manualCookbooks().filter(c => !this.recipe()?.cookbookIds.includes(c.id)));

  private readonly cookbooks = signal<Cookbook[]>([]);

  // Dietary facts review/edit (AI-detected facts become user-confirmed here)
  readonly FactsStatus = DietaryFactsStatus;
  readonly classLabel = ingredientClassLabel;
  readonly factsEditing = signal(false);
  readonly factsDraft = signal<string[]>([]);
  readonly savingFacts = signal(false);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
    private cookbooksService: CookbooksService,
    private measurementService: MeasurementService,
    private familyMembersService: FamilyMembersService,
    private plannedMealsService: PlannedMealsService,
    private statisticsService: StatisticsService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.router.navigate(['/recipes']);
      return;
    }

    this.recipesService.getRecipe(id).subscribe({
      next: recipe => {
        this.recipe.set(recipe);
        this.selectedServings.set(recipe.servings);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load the recipe', 'Dismiss', { duration: 4000 });
        this.router.navigate(['/recipes']);
      }
    });

    // Favorites and history decorate the page; failures stay silent
    this.statisticsService.getDishStatistics().subscribe({
      next: stats => this.dishStatistics.set(stats.dishes),
      error: () => { /* non-critical */ }
    });
    this.loadFavorites();
    this.cookbooksService.getCookbooks().subscribe({
      next: cookbooks => this.cookbooks.set(cookbooks),
      error: () => { /* non-critical */ }
    });
  }

  /** Confirms the AI-detected facts as-is (they become user-confirmed) */
  confirmFacts(): void {
    const recipe = this.recipe();
    if (recipe) {
      this.saveFactsInternal(recipe, recipe.dietaryFacts.contains, 'Facts confirmed');
    }
  }

  startFactsEdit(): void {
    this.factsDraft.set([...(this.recipe()?.dietaryFacts.contains ?? [])]);
    this.factsEditing.set(true);
  }

  saveFactsEdit(): void {
    const recipe = this.recipe();
    if (recipe) {
      this.saveFactsInternal(recipe, this.factsDraft(), 'Facts saved');
    }
  }

  private saveFactsInternal(recipe: Recipe, contains: string[], successMessage: string): void {
    this.savingFacts.set(true);
    this.recipesService.setRecipeFacts(recipe.id, contains).subscribe({
      next: facts => {
        this.savingFacts.set(false);
        this.factsEditing.set(false);
        this.recipe.set({ ...recipe, dietaryFacts: facts });
        this.snackBar.open(successMessage, 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.savingFacts.set(false);
        this.snackBar.open('Could not save the facts', 'Dismiss', { duration: 4000 });
      }
    });
  }

  /** Adds or removes this recipe in a manual collection */
  toggleCookbook(cookbook: Cookbook): void {
    const recipe = this.recipe();
    if (!recipe) {
      return;
    }

    const isMember = recipe.cookbookIds.includes(cookbook.id);
    const request: Observable<unknown> = isMember
      ? this.cookbooksService.removeRecipe(cookbook.id, recipe.id)
      : this.cookbooksService.addRecipes(cookbook.id, [recipe.id]);

    request.subscribe({
      next: () => {
        const cookbookIds = isMember
          ? recipe.cookbookIds.filter(id => id !== cookbook.id)
          : [...recipe.cookbookIds, cookbook.id];
        this.recipe.set({ ...recipe, cookbookIds });
      },
      error: () => this.snackBar.open('Could not update the collection', 'Dismiss', { duration: 4000 })
    });
  }

  private loadFavorites(): void {
    this.familyMembersService.getMembers().subscribe({
      next: members => {
        this.members.set(members);
        if (members.length === 0) {
          return;
        }
        forkJoin(members.map(m => this.familyMembersService.getFavorites(m.id))).subscribe({
          next: favoriteLists => {
            const map = new Map<string, FamilyMemberFavorite[]>();
            members.forEach((member, index) => map.set(member.id, favoriteLists[index]));
            this.favoritesByMember.set(map);
          },
          error: () => { /* non-critical */ }
        });
      },
      error: () => { /* non-critical */ }
    });
  }

  /** This recipe's favorite entry of a member, when it is one */
  favoriteEntry(memberId: string): FamilyMemberFavorite | null {
    const recipeId = this.recipe()?.id;
    return this.favoritesByMember().get(memberId)
      ?.find(f => f.recipeId === recipeId) ?? null;
  }

  toggleFavorite(member: FamilyMember): void {
    const recipe = this.recipe();
    if (!recipe) {
      return;
    }

    const existing = this.favoriteEntry(member.id);
    const request: Observable<unknown> = existing
      ? this.familyMembersService.deleteFavorite(member.id, existing.id)
      : this.familyMembersService.addFavorite(member.id, { recipeId: recipe.id, dishName: recipe.title });

    request.subscribe({
      next: () => this.loadFavorites(),
      error: () => this.snackBar.open('Could not update the favorite', 'Dismiss', { duration: 4000 })
    });
  }

  /**
   * Opens the shared star-rating dialog (the history page's component) for the
   * most recent past occurrence of this dish — ratings always belong to a
   * planned meal, not to the recipe itself (see docs/features/meal-feedback.md)
   */
  openRating(): void {
    const recipe = this.recipe();
    if (!recipe) {
      return;
    }

    const to = new Date();
    to.setDate(to.getDate() - 1);
    const from = new Date(to);
    from.setDate(from.getDate() - 90);

    this.plannedMealsService.getMeals(toIso(from), toIso(to)).subscribe({
      next: meals => {
        const title = recipe.title.trim().toLowerCase();
        const occurrence = meals
          .filter(m => m.recipeId === recipe.id
            || (m.dishName ?? '').trim().toLowerCase() === title)
          .sort((a, b) => b.date.localeCompare(a.date))[0];

        if (!occurrence) {
          this.snackBar.open(
            'This dish was not planned in the last 90 days — rate it from Past Dishes',
            'Dismiss', { duration: 5000 });
          return;
        }

        const data: MealRatingDialogData = { meal: occurrence, members: this.members() };
        this.dialog.open(MealRatingDialog, { data })
          .afterClosed()
          .subscribe(() => this.refreshStatistics());
      },
      error: () => this.snackBar.open('Could not load the meal history', 'Dismiss', { duration: 4000 })
    });
  }

  private refreshStatistics(): void {
    this.statisticsService.getDishStatistics().subscribe({
      next: stats => this.dishStatistics.set(stats.dishes),
      error: () => { /* non-critical */ }
    });
  }

  openQuickPlan(): void {
    const recipe = this.recipe();
    if (!recipe || this.quickPlanLoading()) {
      return;
    }

    const start = new Date();
    const end = new Date(start);
    end.setDate(end.getDate() + 6);
    this.quickPlanLoading.set(true);

    forkJoin({
      meals: this.plannedMealsService.getMeals(toIso(start), toIso(end)),
      members: this.familyMembersService.getMembers()
    }).subscribe({
      next: ({ meals, members }) => {
        this.quickPlanLoading.set(false);
        const data: QuickPlanDialogData = {
          recipe: { id: recipe.id, title: recipe.title },
          startDate: toIso(start),
          meals,
          members
        };
        this.dialog.open<QuickPlanDialog, QuickPlanDialogData, CreatePlannedMeal>(
          QuickPlanDialog,
          { data }
        ).afterClosed().subscribe(result => {
          if (!result) {
            return;
          }

          this.quickPlanLoading.set(true);
          this.plannedMealsService.createMeal(result).subscribe({
            next: () => {
              this.quickPlanLoading.set(false);
              this.snackBar.open(
                `Planned ${recipe.title} for ${this.planDateLabel(result.date)}`,
                'Dismiss',
                { duration: 3500 }
              );
              this.refreshStatistics();
            },
            error: () => {
              this.quickPlanLoading.set(false);
              this.snackBar.open('Could not plan the recipe', 'Dismiss', { duration: 4000 });
            }
          });
        });
      },
      error: () => {
        this.quickPlanLoading.set(false);
        this.snackBar.open('Could not load the upcoming plan', 'Dismiss', { duration: 4000 });
      }
    });
  }

  private planDateLabel(iso: string): string {
    const date = new Date(`${iso}T00:00:00`);
    return new Intl.DateTimeFormat(undefined, {
      weekday: 'long',
      day: 'numeric',
      month: 'long'
    }).format(date);
  }

  remove(): void {
    const recipe = this.recipe();
    if (!recipe) {
      return;
    }

    this.recipesService.deleteRecipe(recipe.id).subscribe({
      next: () => {
        this.snackBar.open('Recipe deleted', 'Dismiss', { duration: 3000 });
        this.router.navigate(['/recipes']);
      },
      error: () => this.snackBar.open('Could not delete the recipe', 'Dismiss', { duration: 4000 })
    });
  }

  adjustServings(delta: number): void {
    const next = Math.min(100, Math.max(1, this.selectedServings() + delta));
    this.selectedServings.set(next);

    // Verbatim source lines describe the recipe's original serving count and cannot
    // be safely rewritten. Keep scaled displays on normalized, structured values.
    if (next !== this.recipe()?.servings) {
      this.showOriginal.set(false);
    }
  }

  formatQuantity(quantity?: number, unit?: string): string {
    // Honors the household measurement preference (metric default)
    const baseServings = this.recipe()?.servings ?? 0;
    const scaledQuantity = quantity == null || baseServings < 1
      ? quantity
      : quantity * this.selectedServings() / baseServings;
    return this.measurementService.format(scaledQuantity, unit);
  }
}
