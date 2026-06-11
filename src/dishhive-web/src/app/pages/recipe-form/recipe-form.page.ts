import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { COMMA, ENTER } from '@angular/cdk/keycodes';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipInputEvent, MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RecipesService } from '../../services/recipes.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { CreateRecipe } from '../../models/recipe.model';

interface IngredientRow {
  name: string;
  quantity: number | null;
  unit: string;
}

interface StepRow {
  instruction: string;
}

/**
 * Manual recipe create/edit form. Ingredients and steps are replaced wholesale on
 * save (see docs/features/recipe-store.md).
 */
@Component({
  selector: 'app-recipe-form-page',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipe-form.page.html',
  styleUrl: './recipe-form.page.scss'
})
export class RecipeFormPage implements OnInit {
  readonly separatorKeys = [ENTER, COMMA] as const;

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly editingId = signal<string | null>(null);

  title = '';
  description = '';
  servings = 4;
  prepTimeMinutes: number | null = null;
  cookTimeMinutes: number | null = null;
  category = '';
  keywords = '';
  imageUrl = '';
  videoUrl = '';
  ingredients: IngredientRow[] = [{ name: '', quantity: null, unit: '' }];
  steps: StepRow[] = [{ instruction: '' }];

  // Tags as signals so the autocomplete suggestions stay reactive
  readonly tags = signal<string[]>([]);
  readonly tagInput = signal('');
  private readonly knownTags = signal<string[]>([]);
  readonly tagOptions = computed(() => {
    const query = this.tagInput().trim().toLowerCase();
    const selected = this.tags().map(t => t.toLowerCase());
    return this.knownTags()
      .filter(name => !selected.includes(name.toLowerCase()))
      .filter(name => !query || name.toLowerCase().includes(query));
  });

  /** Known ingredient names, so existing spellings win over new variants */
  private readonly knownIngredients = signal<string[]>([]);

  /** When set, the new recipe is linked to this planned meal after saving
   *  (entry point: the shopping list's "still to decide" section) */
  private linkMealId: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
    private plannedMealsService: PlannedMealsService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.recipesService.getRecipeTags().subscribe({
      next: tags => this.knownTags.set(tags.map(t => t.name)),
      error: () => { /* autocomplete is a convenience; typing tags still works */ }
    });

    this.recipesService.getIngredientNames().subscribe({
      next: names => this.knownIngredients.set(names),
      error: () => { /* autocomplete is a convenience; typing ingredients still works */ }
    });

    const queryParams = this.route.snapshot.queryParamMap;
    this.linkMealId = queryParams.get('linkMealId');
    const prefilledTitle = queryParams.get('title');
    if (prefilledTitle) {
      this.title = prefilledTitle;
    }

    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.editingId.set(id);
    this.loading.set(true);
    this.recipesService.getRecipe(id).subscribe({
      next: recipe => {
        this.title = recipe.title;
        this.description = recipe.description ?? '';
        this.servings = recipe.servings;
        this.prepTimeMinutes = recipe.prepTimeMinutes ?? null;
        this.cookTimeMinutes = recipe.cookTimeMinutes ?? null;
        this.category = recipe.category ?? '';
        this.keywords = recipe.keywords ?? '';
        this.imageUrl = recipe.imageUrl ?? '';
        this.videoUrl = recipe.videoUrl ?? '';
        this.ingredients = recipe.ingredients.length > 0
          ? recipe.ingredients.map(i => ({ name: i.name, quantity: i.quantity ?? null, unit: i.unit ?? '' }))
          : [{ name: '', quantity: null, unit: '' }];
        this.steps = recipe.steps.length > 0
          ? recipe.steps.map(s => ({ instruction: s.instruction }))
          : [{ instruction: '' }];
        this.tags.set([...recipe.tags]);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load the recipe', 'Dismiss', { duration: 4000 });
        this.router.navigate(['/recipes']);
      }
    });
  }

  addIngredient(): void {
    this.ingredients.push({ name: '', quantity: null, unit: '' });
  }

  /** Ingredient suggestions matching the typed text (top 8, only while typing) */
  ingredientOptions(typed: string): string[] {
    const query = typed.trim().toLowerCase();
    if (!query) {
      return [];
    }
    return this.knownIngredients()
      .filter(name => name.toLowerCase().includes(query) && name.toLowerCase() !== query)
      .slice(0, 8);
  }

  removeIngredient(index: number): void {
    this.ingredients.splice(index, 1);
  }

  addTagFromInput(event: MatChipInputEvent): void {
    this.addTag(event.value);
    event.chipInput.clear();
  }

  selectTag(event: MatAutocompleteSelectedEvent): void {
    this.addTag(event.option.viewValue);
    event.option.deselect();
  }

  removeTag(tag: string): void {
    this.tags.update(tags => tags.filter(t => t !== tag));
  }

  private addTag(value: string): void {
    const name = value.trim().slice(0, 50);
    if (!name) {
      return;
    }
    this.tags.update(tags =>
      tags.some(t => t.toLowerCase() === name.toLowerCase()) ? tags : [...tags, name]);
    this.tagInput.set('');
  }

  addStep(): void {
    this.steps.push({ instruction: '' });
  }

  removeStep(index: number): void {
    this.steps.splice(index, 1);
  }

  canSave(): boolean {
    return this.title.trim().length > 0 && this.servings >= 1;
  }

  save(): void {
    if (!this.canSave()) {
      return;
    }

    // A typed-but-unconfirmed tag still counts (no lost input on save)
    this.addTag(this.tagInput());

    const payload: CreateRecipe = {
      title: this.title.trim(),
      description: this.description.trim() || undefined,
      servings: this.servings,
      prepTimeMinutes: this.prepTimeMinutes ?? undefined,
      cookTimeMinutes: this.cookTimeMinutes ?? undefined,
      totalTimeMinutes: this.prepTimeMinutes || this.cookTimeMinutes
        ? (this.prepTimeMinutes ?? 0) + (this.cookTimeMinutes ?? 0)
        : undefined,
      category: this.category.trim() || undefined,
      keywords: this.keywords.trim() || undefined,
      imageUrl: this.imageUrl.trim() || undefined,
      videoUrl: this.videoUrl.trim() || undefined,
      ingredients: this.ingredients
        .filter(i => i.name.trim())
        .map(i => ({
          name: i.name.trim(),
          quantity: i.quantity ?? undefined,
          unit: i.unit.trim() || undefined
        })),
      steps: this.steps
        .filter(s => s.instruction.trim())
        .map(s => ({ instruction: s.instruction.trim() })),
      tags: this.tags()
    };

    this.saving.set(true);
    const editingId = this.editingId();
    const request = editingId
      ? this.recipesService.updateRecipe(editingId, payload)
      : this.recipesService.createRecipe(payload);

    request.subscribe({
      next: recipe => {
        this.saving.set(false);
        if (this.linkMealId) {
          this.linkToMealAndReturn(this.linkMealId, recipe.id, recipe.title);
        } else {
          this.router.navigate(['/recipes', recipe.id]);
        }
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not save the recipe', 'Dismiss', { duration: 4000 });
      }
    });
  }

  /** Completes the shopping list flow: attach the new recipe to the meal, go back */
  private linkToMealAndReturn(mealId: string, recipeId: string, title: string): void {
    this.plannedMealsService.setRecipe(mealId, recipeId).subscribe({
      next: () => {
        this.snackBar.open(`"${title}" created and linked to the plan`, 'Dismiss', { duration: 4000 });
        this.router.navigate(['/shopping-list']);
      },
      error: () => {
        // The recipe exists; only the link failed — land on the recipe instead
        this.snackBar.open('Recipe saved, but linking to the meal failed', 'Dismiss', { duration: 5000 });
        this.router.navigate(['/recipes', recipeId]);
      }
    });
  }
}
