import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable, forkJoin } from 'rxjs';
import { RecipesService } from '../../services/recipes.service';
import { MeasurementService } from '../../services/measurement.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { StatisticsService } from '../../services/statistics.service';
import { MealRatingDialog, MealRatingDialogData } from '../../components/meal-rating-dialog/meal-rating-dialog';
import { Recipe } from '../../models/recipe.model';
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
    DatePipe,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatListModule,
    MatMenuModule,
    MatProgressSpinnerModule,
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

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
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

  formatQuantity(quantity?: number, unit?: string): string {
    // Honors the household measurement preference (metric default)
    return this.measurementService.format(quantity, unit);
  }
}
