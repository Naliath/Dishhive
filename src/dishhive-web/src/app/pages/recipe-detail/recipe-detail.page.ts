import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RecipesService } from '../../services/recipes.service';
import { SettingsService } from '../../services/settings.service';
import { Recipe } from '../../models/recipe.model';

@Component({
  selector: 'app-recipe-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatDividerModule,
    MatSnackBarModule,
  ],
  template: `
    <div class="page-container narrow">
      <div class="back-row">
        <button mat-button routerLink="/recipes">
          <mat-icon>arrow_back</mat-icon> Back to recipes
        </button>
        @if (recipe()) {
          <div class="header-actions">
            <button mat-stroked-button [routerLink]="['/recipes', recipe()!.id, 'edit']">
              <mat-icon>edit</mat-icon> Edit
            </button>
            <button mat-stroked-button color="warn" (click)="deleteRecipe()">
              <mat-icon>delete_outline</mat-icon> Delete
            </button>
          </div>
        }
      </div>

      @if (loading()) {
        <div class="loading-container"><mat-spinner diameter="48" /></div>
      } @else if (!recipe()) {
        <div class="empty-state">
          <mat-icon>error_outline</mat-icon>
          <h3>Recipe not found</h3>
        </div>
      } @else {
        @let r = recipe()!;
        @if (r.pictureUrl) {
          <img [src]="r.pictureUrl" [alt]="r.title" class="hero-image" />
        }
        <h1 class="recipe-title">{{ r.title }}</h1>
        <div class="meta-row">
          <span class="meta-item"><mat-icon>people</mat-icon> {{ r.servings }} servings</span>
          @if (r.prepTimeMinutes) {
            <span class="meta-item"><mat-icon>timer</mat-icon> {{ r.prepTimeMinutes }} min prep</span>
          }
          @if (r.cookTimeMinutes) {
            <span class="meta-item"><mat-icon>local_fire_department</mat-icon> {{ r.cookTimeMinutes }} min cook</span>
          }
        </div>
        @if (r.tags.length) {
          <mat-chip-set style="margin-bottom:16px;">
            @for (tag of r.tags; track tag) { <mat-chip>{{ tag }}</mat-chip> }
          </mat-chip-set>
        }
        @if (r.description) { <p class="description">{{ r.description }}</p> }
        <mat-divider style="margin:16px 0;" />
        <h2>Ingredients</h2>
        <ul class="ingredient-list">
          @for (ing of r.ingredients; track ing.id) {
            <li>
              @if (ing.quantity) { <strong>{{ ing.quantity | number:'1.0-2' }} {{ ing.unit }}</strong> }
              @else if (ing.originalQuantity) { <strong>{{ ing.originalQuantity }} {{ ing.originalUnit }}</strong> }
              {{ ing.name }}
              @if (ing.notes) { <em class="ing-notes">({{ ing.notes }})</em> }
            </li>
          }
        </ul>
        <mat-divider style="margin:16px 0;" />
        <h2>Method</h2>
        <ol class="step-list">
          @for (step of r.steps; track step.id) { <li>{{ step.instruction }}</li> }
        </ol>
        @if (r.sourceUrl) {
          <mat-divider style="margin:16px 0;" />
          <p class="source-link">
            Source: <a [href]="r.sourceUrl" target="_blank" rel="noopener">{{ r.sourceName || r.sourceUrl }}</a>
          </p>
        }
      }
    </div>
  `,
  styles: [`
    .back-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .header-actions { display: flex; gap: 8px; }
    .hero-image { width: 100%; max-height: 320px; object-fit: cover; border-radius: 12px; margin-bottom: 20px; }
    .recipe-title { font-size: 1.8rem; margin-bottom: 12px; }
    .meta-row { display: flex; flex-wrap: wrap; gap: 16px; margin-bottom: 12px; }
    .meta-item { display: flex; align-items: center; gap: 4px; color: var(--mat-sys-on-surface-variant); font-size: 0.9rem; }
    .meta-item mat-icon { font-size: 18px; height: 18px; width: 18px; }
    .description { color: var(--mat-sys-on-surface-variant); margin-bottom: 8px; }
    .ingredient-list { padding-left: 20px; line-height: 2; }
    .ing-notes { color: var(--mat-sys-on-surface-variant); }
    .step-list { padding-left: 20px; }
    .step-list li { margin-bottom: 8px; line-height: 1.6; }
    .source-link { font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .loading-container { display: flex; justify-content: center; margin-top: 80px; }
  `]
})
export class RecipeDetailPage implements OnInit {
  recipe = signal<Recipe | null>(null);
  loading = signal(true);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
    private settings: SettingsService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) { this.loading.set(false); return; }
    const units = this.settings.measurementSystem() === 'imperial' ? 'imperial' : undefined;
    this.recipesService.getById(id, units).subscribe({
      next: (r) => { this.recipe.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  deleteRecipe(): void {
    const r = this.recipe();
    if (!r || !confirm(`Delete "${r.title}"? This cannot be undone.`)) return;
    this.recipesService.delete(r.id).subscribe({
      next: () => {
        this.snackBar.open('Recipe deleted', undefined, { duration: 2000 });
        this.router.navigate(['/recipes']);
      },
      error: () => this.snackBar.open('Failed to delete recipe', 'Dismiss', { duration: 3000 })
    });
  }
}

