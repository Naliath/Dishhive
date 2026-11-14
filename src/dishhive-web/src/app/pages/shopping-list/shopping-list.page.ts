import { LanguageService as UiLanguageService } from '../../services/language.service';
import { TranslatePipe } from '../../services/language.service';
import { HouseholdClockService } from '../../services/household-clock.service';
import { inject, ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { LocalizedDatePipe } from '../../services/language.service';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { CookingLoaderComponent } from '../../components/cooking-loader/cooking-loader';
import { ShoppingListService } from '../../services/shopping-list.service';
import { MeasurementService } from '../../services/measurement.service';
import { RecipesService } from '../../services/recipes.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { SettingsService } from '../../services/settings.service';
import { ShoppingList, ShoppingListReminder } from '../../models/shopping-list.model';
import { RecipeListItem } from '../../models/recipe.model';

function toIso(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

@Component({
  selector: 'app-shopping-list-page',
  standalone: true,
  imports: [TranslatePipe,
    CookingLoaderComponent,
    LocalizedDatePipe,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatSnackBarModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shopping-list.page.html',
  styleUrl: './shopping-list.page.scss'
})
export class ShoppingListPage implements OnInit {
  readonly uiLanguage = inject(UiLanguageService);
  readonly clock = inject(HouseholdClockService);
  readonly list = signal<ShoppingList | null>(null);
  readonly loading = signal(true);

  /** The reminder whose inline recipe search is open (one at a time) */
  readonly linkingMealId = signal<string | null>(null);
  readonly linkRecipeResults = signal<RecipeListItem[]>([]);
  linkSearch = '';

  private from = '';
  private to = '';
  private recipeSearchSequence = 0;

  constructor(
    private route: ActivatedRoute,
    private shoppingListService: ShoppingListService,
    private recipesService: RecipesService,
    private plannedMealsService: PlannedMealsService,
    public measurementService: MeasurementService,
    private settingsService: SettingsService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    // Range from query params (planner entry point) or default to the current week
    const params = this.route.snapshot.queryParamMap;
    const weekStart = this.settingsService.startOfWeek(this.clock.date());
    const weekEnd = new Date(weekStart);
    weekEnd.setDate(weekEnd.getDate() + 6);

    this.from = params.get('from') ?? toIso(weekStart);
    this.to = params.get('to') ?? toIso(weekEnd);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.shoppingListService.getShoppingList(this.from, this.to).subscribe({
      next: list => {
        this.list.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open(this.uiLanguage.t('ui.couldNotGenerateTheShoppingList'), this.uiLanguage.t('common.dismiss'), { duration: 4000 });
      }
    });
  }

  /** Toggle the inline recipe search for a "still to decide" entry */
  toggleLinking(reminder: ShoppingListReminder): void {
    const opening = this.linkingMealId() !== reminder.plannedMealId;
    this.linkingMealId.set(opening ? reminder.plannedMealId : null);
    this.linkRecipeResults.set([]);
    this.recipeSearchSequence++;
    // The dish text is usually the best starting search
    this.linkSearch = opening ? reminder.text : '';
    if (opening) {
      this.searchLinkRecipes();
    }
  }

  searchLinkRecipes(): void {
    const sequence = ++this.recipeSearchSequence;
    const mealId = this.linkingMealId();
    const term = this.linkSearch.trim();
    if (!term) {
      this.linkRecipeResults.set([]);
      return;
    }
    this.recipesService.getRecipes(term).subscribe({
      next: recipes => {
        if (sequence === this.recipeSearchSequence && mealId === this.linkingMealId())
          this.linkRecipeResults.set(recipes.slice(0, 5));
      },
      error: () => {
        if (sequence === this.recipeSearchSequence && mealId === this.linkingMealId())
          this.linkRecipeResults.set([]);
      }
    });
  }

  linkRecipe(reminder: ShoppingListReminder, recipe: RecipeListItem): void {
    this.plannedMealsService.setRecipe(reminder.plannedMealId, recipe.id).subscribe({
      next: () => {
        this.linkingMealId.set(null);
        this.snackBar.open(this.uiLanguage.t('ui.valueLinkedIngredientsAdded', { p0: (recipe.title) }), this.uiLanguage.t('common.dismiss'), { duration: 3000 });
        this.load();
      },
      error: () => this.snackBar.open(this.uiLanguage.t('ui.couldNotLinkTheRecipe'), this.uiLanguage.t('common.dismiss'), { duration: 4000 })
    });
  }

  /** Query params for "create recipe" — the form links the meal after saving */
  newRecipeParams(reminder: ShoppingListReminder): Record<string, string> {
    return { title: reminder.text, linkMealId: reminder.plannedMealId };
  }

  async copyAsText(): Promise<void> {
    const list = this.list();
    if (!list) {
      return;
    }

    const lines: string[] = [this.uiLanguage.t('ui.shoppingListValueValue', { p0: (list.from), p1: (list.to) }), ''];
    for (const item of list.items) {
      const quantity = this.measurementService.format(item.quantity, item.unit);
      lines.push(quantity ? `- ${quantity} ${item.name}` : `- ${item.name}`);
    }
    if (list.reminders.length > 0) {
      lines.push('', this.uiLanguage.t('ui.stillToDecideb8b735'));
      for (const reminder of list.reminders) {
        lines.push(`- ${reminder.date}: ${reminder.text}`);
      }
    }

    try {
      await navigator.clipboard.writeText(lines.join('\n'));
      this.snackBar.open(this.uiLanguage.t('ui.shoppingListCopiedToClipboard'), this.uiLanguage.t('common.dismiss'), { duration: 3000 });
    } catch {
      this.snackBar.open(this.uiLanguage.t('ui.couldNotAccessTheClipboard'), this.uiLanguage.t('common.dismiss'), { duration: 4000 });
    }
  }
}
