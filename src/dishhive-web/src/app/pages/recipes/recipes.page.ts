import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, forkJoin } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { RecipesService } from '../../services/recipes.service';
import { CookbooksService } from '../../services/cookbooks.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { StatisticsService } from '../../services/statistics.service';
import { Cookbook, RecipeListItem } from '../../models/recipe.model';
import { DishStatistic } from '../../models/statistics.model';

@Component({
  selector: 'app-recipes-page',
  standalone: true,
  imports: [
    DecimalPipe,
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSnackBarModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipes.page.html',
  styleUrl: './recipes.page.scss'
})
export class RecipesPage implements OnInit {
  readonly recipes = signal<RecipeListItem[]>([]);
  readonly loading = signal(true);
  readonly importing = signal(false);
  readonly importVisible = signal(false);

  searchTerm = '';
  importUrl = '';

  // Library filter (category + tags), combinable with an active collection
  readonly categories = signal<string[]>([]);
  readonly knownTags = signal<string[]>([]);
  readonly selectedCategory = signal<string | null>(null);
  readonly selectedTags = signal<string[]>([]);

  readonly cookbooks = signal<Cookbook[]>([]);
  readonly activeCookbookId = signal<string | null>(null);
  readonly savingCookbook = signal(false);
  cookbookName = '';

  readonly hasFilter = computed(() =>
    this.selectedCategory() !== null || this.selectedTags().length > 0 || this.searchTerm.trim().length > 0);

  /** Manual collections a recipe can be added to (auto collections are computed) */
  readonly manualCookbooks = computed(() => this.cookbooks().filter(c => c.kind === 'manual'));

  readonly activeCookbook = computed(() =>
    this.cookbooks().find(c => c.id === this.activeCookbookId()) ?? null);

  /** Dish statistics by lowercased title and favorite counts by recipe id (card decorations) */
  private readonly statsByTitle = signal<Map<string, DishStatistic>>(new Map());
  private readonly favoriteCounts = signal<Map<string, number>>(new Map());

  /** Search keystrokes, debounced so the grid doesn't flash on every letter */
  private readonly searchInput$ = new Subject<string>();
  /** The full-page spinner only shows before the first results arrive */
  private firstLoad = true;

  constructor(
    private recipesService: RecipesService,
    private cookbooksService: CookbooksService,
    private familyMembersService: FamilyMembersService,
    private statisticsService: StatisticsService,
    private snackBar: MatSnackBar,
    private router: Router
  ) {
    this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe(() => this.onFilterChanged());
  }

  ngOnInit(): void {
    this.loadRecipes();
    this.loadFilterSources();
    this.loadCookbooks();
    this.loadDecorations();
  }

  loadRecipes(): void {
    if (this.firstLoad) {
      this.loading.set(true);
    }
    this.recipesService.getRecipes(
      this.searchTerm.trim() || undefined,
      this.selectedCategory() ?? undefined,
      this.selectedTags(),
      this.activeCookbookId() ?? undefined
    ).subscribe({
      next: recipes => {
        this.recipes.set(recipes);
        this.firstLoad = false;
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load recipes', 'Dismiss', { duration: 4000 });
      }
    });
  }

  /** Search keystrokes go through a debounce before hitting the API */
  onSearchInput(): void {
    this.searchInput$.next(this.searchTerm);
  }

  /** Manual filter changes apply within the active collection (when one is selected) */
  onFilterChanged(): void {
    this.loadRecipes();
  }

  onCategoryChange(category: string | null): void {
    this.selectedCategory.set(category);
    this.onFilterChanged();
  }

  onTagsChange(tags: string[]): void {
    this.selectedTags.set(tags);
    this.onFilterChanged();
  }

  /** Tap a collection chip to view its recipes; tap again to deselect */
  applyCookbook(cookbook: Cookbook): void {
    this.activeCookbookId.set(this.activeCookbookId() === cookbook.id ? null : cookbook.id);
    this.loadRecipes();
  }

  clearFilter(): void {
    this.searchTerm = '';
    this.selectedCategory.set(null);
    this.selectedTags.set([]);
    this.activeCookbookId.set(null);
    this.loadRecipes();
  }

  createCookbook(): void {
    const name = this.cookbookName.trim();
    if (!name) {
      return;
    }

    this.savingCookbook.set(true);
    this.cookbooksService.createCookbook(name).subscribe({
      next: cookbook => {
        this.savingCookbook.set(false);
        this.cookbookName = '';
        this.activeCookbookId.set(cookbook.id);
        this.loadCookbooks();
        this.loadRecipes();
        this.snackBar.open(
          `Collection "${cookbook.name}" created — add recipes via the card menus`, 'Dismiss', { duration: 4000 });
      },
      error: (error: HttpErrorResponse) => {
        this.savingCookbook.set(false);
        const detail = error.error?.detail ?? 'Could not create the collection';
        this.snackBar.open(detail, 'Dismiss', { duration: 4000 });
      }
    });
  }

  deleteCookbook(cookbook: Cookbook, event: Event): void {
    event.stopPropagation();
    this.cookbooksService.deleteCookbook(cookbook.id).subscribe({
      next: () => {
        if (this.activeCookbookId() === cookbook.id) {
          this.activeCookbookId.set(null);
          this.loadRecipes();
        }
        this.loadCookbooks();
      },
      error: () => this.snackBar.open('Could not delete the collection', 'Dismiss', { duration: 4000 })
    });
  }

  addToCookbook(recipe: RecipeListItem, cookbook: Cookbook): void {
    this.cookbooksService.addRecipes(cookbook.id, [recipe.id]).subscribe({
      next: () => {
        this.loadCookbooks();
        this.snackBar.open(`"${recipe.title}" added to ${cookbook.name}`, 'Dismiss', { duration: 3000 });
      },
      error: () => this.snackBar.open('Could not add to the collection', 'Dismiss', { duration: 4000 })
    });
  }

  /** Available while viewing a manual collection */
  removeFromActiveCookbook(recipe: RecipeListItem): void {
    const cookbook = this.activeCookbook();
    if (!cookbook || cookbook.kind !== 'manual') {
      return;
    }

    this.cookbooksService.removeRecipe(cookbook.id, recipe.id).subscribe({
      next: () => {
        this.loadCookbooks();
        this.loadRecipes();
        this.snackBar.open(`"${recipe.title}" removed from ${cookbook.name}`, 'Dismiss', { duration: 3000 });
      },
      error: () => this.snackBar.open('Could not remove from the collection', 'Dismiss', { duration: 4000 })
    });
  }

  statsOf(recipe: RecipeListItem): DishStatistic | null {
    return this.statsByTitle().get(recipe.title.trim().toLowerCase()) ?? null;
  }

  favoriteCountOf(recipe: RecipeListItem): number {
    return this.favoriteCounts().get(recipe.id) ?? 0;
  }

  toggleImport(): void {
    this.importVisible.update(visible => !visible);
  }

  import(): void {
    const url = this.importUrl.trim();
    if (!url) {
      return;
    }

    this.importing.set(true);
    this.recipesService.importRecipe(url).subscribe({
      next: recipe => {
        this.importing.set(false);
        this.importUrl = '';
        this.importVisible.set(false);
        this.snackBar.open(`Imported "${recipe.title}" — check the values`, 'Dismiss', { duration: 5000 });
        this.router.navigate(['/recipes', recipe.id]);
      },
      error: (error: HttpErrorResponse) => {
        this.importing.set(false);
        const detail = error.error?.title ?? 'Import failed';
        this.snackBar.open(detail, 'Dismiss', { duration: 5000 });
      }
    });
  }

  private loadFilterSources(): void {
    this.recipesService.getCategories().subscribe({
      next: categories => this.categories.set(categories),
      error: () => { /* the filter degrades to search-only */ }
    });
    this.recipesService.getRecipeTags().subscribe({
      next: tags => this.knownTags.set(tags.map(t => t.name)),
      error: () => { /* the filter degrades to search-only */ }
    });
  }

  private loadCookbooks(): void {
    this.cookbooksService.getCookbooks().subscribe({
      next: cookbooks => this.cookbooks.set(cookbooks),
      error: () => { /* cookbooks are a convenience on top of the filter */ }
    });
  }

  /** Ratings and favorite counts decorate the cards; failures stay silent */
  private loadDecorations(): void {
    this.statisticsService.getDishStatistics().subscribe({
      next: stats => this.statsByTitle.set(
        new Map(stats.dishes.map(d => [d.dishName.toLowerCase(), d]))),
      error: () => { /* non-critical */ }
    });

    this.familyMembersService.getMembers().subscribe({
      next: members => {
        if (members.length === 0) {
          return;
        }
        forkJoin(members.map(m => this.familyMembersService.getFavorites(m.id))).subscribe({
          next: favoriteLists => {
            const counts = new Map<string, number>();
            for (const favorite of favoriteLists.flat()) {
              if (favorite.recipeId) {
                counts.set(favorite.recipeId, (counts.get(favorite.recipeId) ?? 0) + 1);
              }
            }
            this.favoriteCounts.set(counts);
          },
          error: () => { /* non-critical */ }
        });
      },
      error: () => { /* non-critical */ }
    });
  }
}
