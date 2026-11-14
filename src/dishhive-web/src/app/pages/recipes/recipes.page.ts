import { LanguageService as UiLanguageService } from '../../services/language.service';
import { TranslatePipe } from '../../services/language.service';
import { inject, ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, forkJoin } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { CookingLoaderComponent } from '../../components/cooking-loader/cooking-loader';
import { RecipesService } from '../../services/recipes.service';
import { CookbooksService } from '../../services/cookbooks.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { StatisticsService } from '../../services/statistics.service';
import { Cookbook, RecipeListItem } from '../../models/recipe.model';
import { DishStatistic } from '../../models/statistics.model';
import { AutoCollectionNamePipe } from '../../services/language.service';

@Component({
  selector: 'app-recipes-page',
  standalone: true,
  imports: [TranslatePipe,
    CookingLoaderComponent,
    DecimalPipe,
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatSelectModule,
    MatPaginatorModule,
    MatSnackBarModule,
    MatTooltipModule,
    AutoCollectionNamePipe
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipes.page.html',
  styleUrl: './recipes.page.scss'
})
export class RecipesPage implements OnInit {
  readonly uiLanguage = inject(UiLanguageService);
  readonly recipes = signal<RecipeListItem[]>([]);
  readonly loading = signal(true);
  readonly importing = signal(false);
  readonly importVisible = signal(false);
  readonly totalRecipes = signal(0);
  readonly pageIndex = signal(0);
  readonly pageSize = signal(24);

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
  private recipeLoadSequence = 0;

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
    const sequence = ++this.recipeLoadSequence;
    if (this.firstLoad) {
      this.loading.set(true);
    }
    this.recipesService.getRecipesPage(
      this.searchTerm.trim() || undefined,
      this.selectedCategory() ?? undefined,
      this.selectedTags(),
      this.activeCookbookId() ?? undefined,
      this.pageIndex() + 1,
      this.pageSize()
    ).subscribe({
      next: page => {
        if (sequence !== this.recipeLoadSequence) return;
        this.recipes.set(page.items);
        this.totalRecipes.set(page.totalCount);
        this.firstLoad = false;
        this.loading.set(false);
      },
      error: () => {
        if (sequence !== this.recipeLoadSequence) return;
        this.loading.set(false);
        this.snackBar.open(this.uiLanguage.t('ui.couldNotLoadRecipes'), this.uiLanguage.t('common.dismiss'), { duration: 4000 });
      }
    });
  }

  /** Search keystrokes go through a debounce before hitting the API */
  onSearchInput(): void {
    this.searchInput$.next(this.searchTerm);
  }

  /** Manual filter changes apply within the active collection (when one is selected) */
  onFilterChanged(): void {
    this.pageIndex.set(0);
    this.loadRecipes();
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
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
    this.pageIndex.set(0);
    this.loadRecipes();
  }

  clearFilter(): void {
    this.searchTerm = '';
    this.selectedCategory.set(null);
    this.selectedTags.set([]);
    this.activeCookbookId.set(null);
    this.pageIndex.set(0);
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
          this.uiLanguage.t('ui.collectionValueCreatedAddRecipesViaTheCardMenus', { p0: (cookbook.name) }), this.uiLanguage.t('common.dismiss'), { duration: 4000 });
      },
      error: (error: HttpErrorResponse) => {
        this.savingCookbook.set(false);
        const detail = error.error?.detail ?? this.uiLanguage.t('ui.couldNotCreateTheCollection');
        this.snackBar.open(detail, this.uiLanguage.t('common.dismiss'), { duration: 4000 });
      }
    });
  }

  deleteCookbook(cookbook: Cookbook): void {
    this.cookbooksService.deleteCookbook(cookbook.id).subscribe({
      next: () => {
        if (this.activeCookbookId() === cookbook.id) {
          this.activeCookbookId.set(null);
          this.loadRecipes();
        }
        this.loadCookbooks();
      },
      error: () => this.snackBar.open(this.uiLanguage.t('ui.couldNotDeleteTheCollection'), this.uiLanguage.t('common.dismiss'), { duration: 4000 })
    });
  }

  isInCookbook(recipe: RecipeListItem, cookbook: Cookbook): boolean {
    return recipe.cookbookIds.includes(cookbook.id);
  }

  toggleCookbookMembership(recipe: RecipeListItem, cookbook: Cookbook): void {
    const wasMember = this.isInCookbook(recipe, cookbook);
    const request: Observable<unknown> = wasMember
      ? this.cookbooksService.removeRecipe(cookbook.id, recipe.id)
      : this.cookbooksService.addRecipes(cookbook.id, [recipe.id]);

    request.subscribe({
      next: () => {
        this.loadCookbooks();
        this.loadRecipes();
        const action = wasMember ? this.uiLanguage.t('ui.removedFrom') : this.uiLanguage.t('ui.addedTo');
        this.snackBar.open(
          `"${recipe.title}" ${action} ${cookbook.name}`, this.uiLanguage.t('common.dismiss'), { duration: 3000 });
      },
      error: () => this.snackBar.open(
        this.uiLanguage.t('ui.couldNotUpdateTheCollection'), this.uiLanguage.t('common.dismiss'), { duration: 4000 })
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
        this.snackBar.open(this.uiLanguage.t('ui.importedValueCheckTheValues', { p0: (recipe.title) }), this.uiLanguage.t('common.dismiss'), { duration: 5000 });
        this.router.navigate(['/recipes', recipe.id]);
      },
      error: (error: HttpErrorResponse) => {
        this.importing.set(false);
        const detail = error.error?.title ?? this.uiLanguage.t('aiStats.reviewOutcomes.importFailed');
        this.snackBar.open(detail, this.uiLanguage.t('common.dismiss'), { duration: 5000 });
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
