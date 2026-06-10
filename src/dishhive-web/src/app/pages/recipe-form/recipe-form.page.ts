import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RecipesService } from '../../services/recipes.service';
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
    MatButtonModule,
    MatCardModule,
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

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
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

  removeIngredient(index: number): void {
    this.ingredients.splice(index, 1);
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
        .map(s => ({ instruction: s.instruction.trim() }))
    };

    this.saving.set(true);
    const editingId = this.editingId();
    const request = editingId
      ? this.recipesService.updateRecipe(editingId, payload)
      : this.recipesService.createRecipe(payload);

    request.subscribe({
      next: recipe => {
        this.saving.set(false);
        this.router.navigate(['/recipes', recipe.id]);
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Could not save the recipe', 'Dismiss', { duration: 4000 });
      }
    });
  }
}
