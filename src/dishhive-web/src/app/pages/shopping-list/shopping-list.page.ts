import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ShoppingListService } from '../../services/shopping-list.service';
import { MeasurementService } from '../../services/measurement.service';
import { RecipesService } from '../../services/recipes.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { ShoppingList, ShoppingListReminder } from '../../models/shopping-list.model';
import { RecipeListItem } from '../../models/recipe.model';

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
  selector: 'app-shopping-list-page',
  standalone: true,
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shopping-list.page.html',
  styleUrl: './shopping-list.page.scss'
})
export class ShoppingListPage implements OnInit {
  readonly list = signal<ShoppingList | null>(null);
  readonly loading = signal(true);

  /** The reminder whose inline recipe search is open (one at a time) */
  readonly linkingMealId = signal<string | null>(null);
  readonly linkRecipeResults = signal<RecipeListItem[]>([]);
  linkSearch = '';

  private from = '';
  private to = '';

  constructor(
    private route: ActivatedRoute,
    private shoppingListService: ShoppingListService,
    private recipesService: RecipesService,
    private plannedMealsService: PlannedMealsService,
    public measurementService: MeasurementService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    // Range from query params (planner entry point) or default to the current week
    const params = this.route.snapshot.queryParamMap;
    const monday = mondayOf(new Date());
    const sunday = new Date(monday);
    sunday.setDate(sunday.getDate() + 6);

    this.from = params.get('from') ?? toIso(monday);
    this.to = params.get('to') ?? toIso(sunday);
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
        this.snackBar.open('Could not generate the shopping list', 'Dismiss', { duration: 4000 });
      }
    });
  }

  /** Toggle the inline recipe search for a "still to decide" entry */
  toggleLinking(reminder: ShoppingListReminder): void {
    const opening = this.linkingMealId() !== reminder.plannedMealId;
    this.linkingMealId.set(opening ? reminder.plannedMealId : null);
    this.linkRecipeResults.set([]);
    // The dish text is usually the best starting search
    this.linkSearch = opening ? reminder.text : '';
    if (opening) {
      this.searchLinkRecipes();
    }
  }

  searchLinkRecipes(): void {
    const term = this.linkSearch.trim();
    if (!term) {
      this.linkRecipeResults.set([]);
      return;
    }
    this.recipesService.getRecipes(term).subscribe({
      next: recipes => this.linkRecipeResults.set(recipes.slice(0, 5)),
      error: () => this.linkRecipeResults.set([])
    });
  }

  linkRecipe(reminder: ShoppingListReminder, recipe: RecipeListItem): void {
    this.plannedMealsService.setRecipe(reminder.plannedMealId, recipe.id).subscribe({
      next: () => {
        this.linkingMealId.set(null);
        this.snackBar.open(`"${recipe.title}" linked — ingredients added`, 'Dismiss', { duration: 3000 });
        this.load();
      },
      error: () => this.snackBar.open('Could not link the recipe', 'Dismiss', { duration: 4000 })
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

    const lines: string[] = [`Shopping list ${list.from} – ${list.to}`, ''];
    for (const item of list.items) {
      const quantity = this.measurementService.format(item.quantity, item.unit);
      lines.push(quantity ? `- ${quantity} ${item.name}` : `- ${item.name}`);
    }
    if (list.reminders.length > 0) {
      lines.push('', 'Still to decide:');
      for (const reminder of list.reminders) {
        lines.push(`- ${reminder.date}: ${reminder.text}`);
      }
    }

    try {
      await navigator.clipboard.writeText(lines.join('\n'));
      this.snackBar.open('Shopping list copied to clipboard', 'Dismiss', { duration: 3000 });
    } catch {
      this.snackBar.open('Could not access the clipboard', 'Dismiss', { duration: 4000 });
    }
  }
}
