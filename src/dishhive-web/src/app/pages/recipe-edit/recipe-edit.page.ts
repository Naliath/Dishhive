import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RecipesService } from '../../services/recipes.service';
import { CreateRecipe } from '../../models/recipe.model';

interface IngredientRow {
  name: string;
  quantity: number | null;
  unit: string;
  notes: string;
}

interface StepRow {
  instruction: string;
}

@Component({
  selector: 'app-recipe-edit',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatDividerModule,
    MatSnackBarModule,
  ],
  template: `
    <div class="page-container narrow">
      <div class="back-row">
        <button mat-button [routerLink]="recipeId() ? ['/recipes', recipeId()] : ['/recipes']">
          <mat-icon>arrow_back</mat-icon>
          {{ recipeId() ? 'Back to recipe' : 'Back to recipes' }}
        </button>
      </div>

      @if (loading()) {
        <div class="loading-container"><mat-spinner diameter="48" /></div>
      } @else {
        <h1 class="section-title">{{ recipeId() ? 'Edit recipe' : 'New recipe' }}</h1>

        <!-- Basic info -->
        <section class="form-section">
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Title *</mat-label>
            <input matInput [(ngModel)]="title" placeholder="e.g. Spaghetti Carbonara" required />
          </mat-form-field>

          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Description</mat-label>
            <textarea matInput [(ngModel)]="description" rows="3"
                      placeholder="Short description of the dish..."></textarea>
          </mat-form-field>

          <div class="row-fields">
            <mat-form-field appearance="outline" style="flex:1;">
              <mat-label>Servings</mat-label>
              <input matInput type="number" [(ngModel)]="servings" min="1" />
            </mat-form-field>
            <mat-form-field appearance="outline" style="flex:1;">
              <mat-label>Prep time (min)</mat-label>
              <input matInput type="number" [(ngModel)]="prepTimeMinutes" min="0" />
            </mat-form-field>
            <mat-form-field appearance="outline" style="flex:1;">
              <mat-label>Cook time (min)</mat-label>
              <input matInput type="number" [(ngModel)]="cookTimeMinutes" min="0" />
            </mat-form-field>
          </div>

          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Picture URL</mat-label>
            <input matInput [(ngModel)]="pictureUrl" placeholder="https://..." />
          </mat-form-field>

          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Tags (comma-separated)</mat-label>
            <input matInput [(ngModel)]="tagsRaw" placeholder="Italian, vegetarian, quick" />
          </mat-form-field>
        </section>

        <mat-divider style="margin:16px 0;" />

        <!-- Ingredients -->
        <section class="form-section">
          <h2 class="section-subtitle">Ingredients</h2>
          @for (ing of ingredients(); track $index; let i = $index) {
            <div class="ingredient-row">
              <mat-form-field appearance="outline" style="flex:3;">
                <mat-label>Name</mat-label>
                <input matInput [(ngModel)]="ing.name" placeholder="e.g. Spaghetti" />
              </mat-form-field>
              <mat-form-field appearance="outline" style="flex:1.2;">
                <mat-label>Quantity</mat-label>
                <input matInput type="number" [(ngModel)]="ing.quantity" min="0" />
              </mat-form-field>
              <mat-form-field appearance="outline" style="flex:1;">
                <mat-label>Unit</mat-label>
                <input matInput [(ngModel)]="ing.unit" placeholder="g, ml, cups…" />
              </mat-form-field>
              <mat-form-field appearance="outline" style="flex:2;">
                <mat-label>Notes</mat-label>
                <input matInput [(ngModel)]="ing.notes" placeholder="finely chopped…" />
              </mat-form-field>
              <button mat-icon-button color="warn" (click)="removeIngredient(i)"
                      [disabled]="ingredients().length === 1" aria-label="Remove ingredient">
                <mat-icon>remove_circle_outline</mat-icon>
              </button>
            </div>
          }
          <button mat-stroked-button (click)="addIngredient()">
            <mat-icon>add</mat-icon> Add ingredient
          </button>
        </section>

        <mat-divider style="margin:16px 0;" />

        <!-- Steps -->
        <section class="form-section">
          <h2 class="section-subtitle">Method</h2>
          @for (step of steps(); track $index; let i = $index) {
            <div class="step-row">
              <span class="step-number">{{ i + 1 }}</span>
              <mat-form-field appearance="outline" style="flex:1;">
                <mat-label>Step {{ i + 1 }}</mat-label>
                <textarea matInput [(ngModel)]="step.instruction" rows="2"
                          placeholder="Describe this step…"></textarea>
              </mat-form-field>
              <button mat-icon-button color="warn" (click)="removeStep(i)"
                      [disabled]="steps().length === 1" aria-label="Remove step">
                <mat-icon>remove_circle_outline</mat-icon>
              </button>
            </div>
          }
          <button mat-stroked-button (click)="addStep()">
            <mat-icon>add</mat-icon> Add step
          </button>
        </section>

        <mat-divider style="margin:24px 0;" />

        <div class="form-actions">
          <button mat-flat-button color="primary" (click)="save()" [disabled]="saving() || !title.trim()">
            <ng-container>
              @if (saving()) { <mat-spinner diameter="20" /> } @else {
                <mat-icon>save</mat-icon>
              }
              {{ recipeId() ? 'Save changes' : 'Create recipe' }}
            </ng-container>
          </button>
          <button mat-stroked-button color="warn"
                  [routerLink]="recipeId() ? ['/recipes', recipeId()] : ['/recipes']">
            Cancel
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    .back-row { margin-bottom: 16px; }
    .form-section { margin-bottom: 8px; }
    .section-subtitle { font-size: 1rem; font-weight: 600; margin: 0 0 12px; }
    .full-width { width: 100%; }
    .row-fields { display: flex; gap: 12px; flex-wrap: wrap; }
    .ingredient-row { display: flex; gap: 8px; align-items: flex-start; flex-wrap: wrap; margin-bottom: 4px; }
    .step-row { display: flex; gap: 8px; align-items: flex-start; margin-bottom: 8px; }
    .step-number { min-width: 28px; font-weight: 600; line-height: 56px; color: var(--mat-sys-on-surface-variant); }
    .form-actions { display: flex; gap: 12px; align-items: center; margin-bottom: 40px; }
    .loading-container { display: flex; justify-content: center; margin-top: 80px; }
  `]
})
export class RecipeEditPage implements OnInit {
  recipeId = signal<string | null>(null);
  loading = signal(false);
  saving = signal(false);

  // Form fields
  title = '';
  description = '';
  servings = 4;
  prepTimeMinutes: number | null = null;
  cookTimeMinutes: number | null = null;
  pictureUrl = '';
  tagsRaw = '';
  ingredients = signal<IngredientRow[]>([{ name: '', quantity: null, unit: '', notes: '' }]);
  steps = signal<StepRow[]>([{ instruction: '' }]);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.recipeId.set(id);
      this.loading.set(true);
      this.recipesService.getById(id).subscribe({
        next: (r) => {
          this.title = r.title;
          this.description = r.description ?? '';
          this.servings = r.servings;
          this.prepTimeMinutes = r.prepTimeMinutes ?? null;
          this.cookTimeMinutes = r.cookTimeMinutes ?? null;
          this.pictureUrl = r.pictureUrl ?? '';
          this.tagsRaw = r.tags?.join(', ') ?? '';
          this.ingredients.set(
            r.ingredients.length > 0
              ? r.ingredients.map(i => ({
                  name: i.name,
                  quantity: i.quantity ?? null,
                  unit: i.unit ?? '',
                  notes: i.notes ?? ''
                }))
              : [{ name: '', quantity: null, unit: '', notes: '' }]
          );
          this.steps.set(
            r.steps.length > 0
              ? r.steps.map(s => ({ instruction: s.instruction }))
              : [{ instruction: '' }]
          );
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.snackBar.open('Failed to load recipe', 'Dismiss', { duration: 3000 });
          this.router.navigate(['/recipes']);
        }
      });
    }
  }

  addIngredient(): void {
    this.ingredients.update(list => [...list, { name: '', quantity: null, unit: '', notes: '' }]);
  }

  removeIngredient(index: number): void {
    this.ingredients.update(list => list.filter((_, i) => i !== index));
  }

  addStep(): void {
    this.steps.update(list => [...list, { instruction: '' }]);
  }

  removeStep(index: number): void {
    this.steps.update(list => list.filter((_, i) => i !== index));
  }

  save(): void {
    if (!this.title.trim()) return;
    this.saving.set(true);

    const tags = this.tagsRaw
      .split(',')
      .map(t => t.trim())
      .filter(t => t.length > 0);

    const payload: CreateRecipe = {
      title: this.title.trim(),
      description: this.description.trim() || undefined,
      servings: this.servings,
      prepTimeMinutes: this.prepTimeMinutes ?? undefined,
      cookTimeMinutes: this.cookTimeMinutes ?? undefined,
      pictureUrl: this.pictureUrl.trim() || undefined,
      tags,
      ingredients: this.ingredients()
        .filter(i => i.name.trim())
        .map((i, idx) => ({
          name: i.name.trim(),
          quantity: i.quantity ?? undefined,
          unit: i.unit.trim() || undefined,
          notes: i.notes.trim() || undefined,
          sortOrder: idx
        })),
      steps: this.steps()
        .filter(s => s.instruction.trim())
        .map((s, idx) => ({
          stepNumber: idx + 1,
          instruction: s.instruction.trim()
        }))
    };

    const id = this.recipeId();
    if (id) {
      this.recipesService.update(id, payload).subscribe({
        next: () => {
          this.saving.set(false);
          this.snackBar.open('Recipe saved', undefined, { duration: 2000 });
          this.router.navigate(['/recipes', id]);
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Failed to save recipe', 'Dismiss', { duration: 3000 });
        }
      });
    } else {
      this.recipesService.create(payload).subscribe({
        next: (result) => {
          this.saving.set(false);
          this.snackBar.open('Recipe created', undefined, { duration: 2000 });
          this.router.navigate(['/recipes', result.id]);
        },
        error: () => {
          this.saving.set(false);
          this.snackBar.open('Failed to create recipe', 'Dismiss', { duration: 3000 });
        }
      });
    }
  }
}
