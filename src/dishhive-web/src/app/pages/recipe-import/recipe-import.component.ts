import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { RecipeImportService, ImportPreviewResult } from '../../services';
import { ImportedRecipe } from '../../models';

@Component({
  selector: 'app-recipe-import',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <h1>Import a recipe</h1>
      <p class="muted">Paste a URL from a supported site and we'll fetch and parse it for you to review.</p>

      <mat-card>
        <mat-card-content class="row">
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Recipe URL</mat-label>
            <input matInput [(ngModel)]="url" placeholder="https://dagelijksekost.vrt.be/gerechten/..." />
          </mat-form-field>
          <button mat-raised-button color="primary" (click)="preview()" [disabled]="loading() || !url">
            @if (loading()) { <mat-progress-spinner diameter="18" mode="indeterminate" /> } @else { Preview }
          </button>
        </mat-card-content>
      </mat-card>

      @if (error()) { <p class="error">{{ error() }}</p> }

      @if (preview$()) {
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ preview$()!.recipe.title }}</mat-card-title>
            <mat-card-subtitle>From {{ preview$()!.recipe.providerKey }} · {{ preview$()!.recipe.servings }} servings</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            @if (preview$()!.usedAgent) {
              <p class="agent-badge">
                ✨ Imported via adaptive AI agent.
                @if (preview$()!.blueprintLearned) {
                  Next time this site is imported, it will be parsed deterministically without AI.
                } @else if (preview$()!.agentNote) {
                  <span class="muted">{{ preview$()!.agentNote }}</span>
                }
              </p>
            }
            @if (preview$()!.recipe.imageUrl) { <img [src]="preview$()!.recipe.imageUrl" alt="" class="hero" /> }
            <p>{{ preview$()!.recipe.description }}</p>

            <h3>Ingredients</h3>
            <ul>
              @for (i of preview$()!.recipe.ingredients; track $index) {
                <li>{{ i.quantity }} {{ i.unit }} {{ i.name }}</li>
              }
            </ul>

            <h3>Steps</h3>
            <ol>
              @for (s of preview$()!.recipe.steps; track $index) { <li>{{ s.text }}</li> }
            </ol>
          </mat-card-content>
          <mat-card-actions>
            <button mat-raised-button color="primary" (click)="save()" [disabled]="saving()">Save to my recipes</button>
            <button mat-button (click)="reset()">Clear</button>
          </mat-card-actions>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .page { padding: 16px; max-width: 800px; margin: 0 auto; }
    .row { display: flex; gap: 8px; align-items: center; }
    .grow { flex: 1; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--mat-sys-error); }
    .agent-badge { background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container); padding: 8px 12px; border-radius: 8px; }
    .hero { max-width: 100%; border-radius: 8px; }
    h3 { margin-block-start: 16px; }
  `],
})
export class RecipeImportComponent {
  private readonly svc = inject(RecipeImportService);
  private readonly router = inject(Router);

  url = '';
  readonly preview$ = signal<ImportPreviewResult | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  async preview() {
    this.error.set(null);
    this.loading.set(true);
    try {
      const r = await firstValueFrom(this.svc.preview(this.url));
      this.preview$.set(r);
    } catch (e: any) {
      this.error.set(e?.error?.message ?? 'Could not preview that URL.');
    } finally {
      this.loading.set(false);
    }
  }

  async save() {
    if (!this.preview$()) return;
    this.saving.set(true);
    try {
      const recipe = await firstValueFrom(this.svc.save(this.preview$()!.recipe));
      this.router.navigate(['/recipes', recipe.id]);
    } finally {
      this.saving.set(false);
    }
  }

  reset() {
    this.preview$.set(null);
    this.url = '';
    this.error.set(null);
  }
}
