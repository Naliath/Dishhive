import { Component, Inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { RecipesService } from '../../services/recipes.service';
import { ImportedRecipe } from '../../models/recipe.model';

@Component({
  selector: 'app-import-recipe-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatIconModule,
    MatChipsModule,
  ],
  template: `
    <h2 mat-dialog-title>Import recipe from URL</h2>

    <mat-dialog-content>
      <div class="import-form">
        @if (!preview()) {
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Recipe URL</mat-label>
            <input matInput [(ngModel)]="url" type="url"
                   placeholder="https://dagelijksekost.vrt.be/gerechten/..."
                   (keydown.enter)="onFetch()" />
            <mat-hint>Supported: dagelijksekost.vrt.be</mat-hint>
          </mat-form-field>

          @if (error()) {
            <p class="error-message">{{ error() }}</p>
          }
        } @else {
          <div class="preview">
            @if (preview()!.pictureUrl) {
              <img [src]="preview()!.pictureUrl" [alt]="preview()!.title" class="preview-image" />
            }
            <h3>{{ preview()!.title }}</h3>
            @if (preview()!.description) {
              <p class="preview-desc">{{ preview()!.description }}</p>
            }
            <div class="preview-meta">
              <span>{{ preview()!.ingredients.length }} ingredients</span>
              <span>{{ preview()!.steps.length }} steps</span>
              @if (preview()!.servings) { <span>{{ preview()!.servings }} servings</span> }
            </div>
            <p class="preview-source">Source: {{ preview()!.sourceName }}</p>
          </div>
        }
      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="onCancel()">Cancel</button>
      @if (!preview()) {
        <button mat-flat-button color="primary" (click)="onFetch()"
                [disabled]="!url.trim() || loading()">
          @if (loading()) { <mat-spinner diameter="18" style="display:inline-block" /> }
          @else { Fetch recipe }
        </button>
      } @else {
        <button mat-button (click)="preview.set(null); error.set(null)">Back</button>
        <button mat-flat-button color="primary" (click)="onSave()" [disabled]="loading()">
          @if (loading()) { <mat-spinner diameter="18" style="display:inline-block" /> }
          @else { Save to my recipes }
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .import-form { min-width: 360px; padding-top: 8px; }
    .full-width { width: 100%; }
    .error-message { color: var(--mat-sys-error); margin-top: 8px; }
    .preview { display: flex; flex-direction: column; gap: 8px; }
    .preview-image { width: 100%; max-height: 200px; object-fit: cover; border-radius: 8px; }
    .preview-desc { color: var(--mat-sys-on-surface-variant); font-size: 0.9rem; margin: 0; }
    .preview-meta { display: flex; gap: 16px; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .preview-source { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); margin: 0; }
  `]
})
export class ImportRecipeDialogComponent {
  url = '';
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly preview = signal<ImportedRecipe | null>(null);

  constructor(
    public dialogRef: MatDialogRef<ImportRecipeDialogComponent, ImportedRecipe | null>,
    @Inject(MAT_DIALOG_DATA) _data: void,
    private recipesService: RecipesService
  ) {}

  onFetch(): void {
    if (!this.url.trim()) return;
    this.loading.set(true);
    this.error.set(null);
    this.recipesService.importFromUrl(this.url.trim()).subscribe({
      next: (result) => { this.preview.set(result); this.loading.set(false); },
      error: (err) => {
        this.error.set(err?.error?.message ?? 'Could not import recipe. Check the URL and try again.');
        this.loading.set(false);
      }
    });
  }

  onSave(): void {
    const current = this.preview();
    if (!current) return;
    this.loading.set(true);
    this.recipesService.importAndSave(current).subscribe({
      next: () => { this.loading.set(false); this.dialogRef.close(current); },
      error: () => { this.error.set('Failed to save recipe.'); this.loading.set(false); }
    });
  }

  onCancel(): void { this.dialogRef.close(null); }
}
