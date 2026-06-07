import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatChipsModule } from '@angular/material/chips';
import { firstValueFrom } from 'rxjs';
import { RecipesService } from '../../services/recipes.service';
import { RecipeSummary } from '../../models';

@Component({
  selector: 'app-recipes-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule,
    MatFormFieldModule, MatInputModule, MatIconModule, MatChipsModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page__header">
        <h1>Recipes</h1>
        <div class="page__actions">
          <a mat-raised-button color="primary" routerLink="/recipes/new"><mat-icon>add</mat-icon> New</a>
          <a mat-stroked-button routerLink="/recipes/import"><mat-icon>cloud_download</mat-icon> Import</a>
        </div>
      </header>

      <mat-form-field appearance="outline" class="search">
        <mat-label>Search</mat-label>
        <input matInput [ngModel]="search()" (ngModelChange)="onSearchChange($event)" />
      </mat-form-field>

      <div class="grid">
        @for (r of recipes(); track r.id) {
          <a class="card" [routerLink]="['/recipes', r.id]">
            <mat-card>
              @if (r.imageUrl) { <img [src]="r.imageUrl" [alt]="r.title" /> }
              <mat-card-content>
                <h3>{{ r.title }}</h3>
                <p class="muted">{{ r.servings }} servings</p>
                <mat-chip-set>
                  @for (t of r.tags; track t) { <mat-chip>{{ t }}</mat-chip> }
                </mat-chip-set>
              </mat-card-content>
            </mat-card>
          </a>
        }
        @if (recipes().length === 0) { <p class="muted">No recipes yet.</p> }
      </div>
    </div>
  `,
  styles: [`
    .page { padding: 16px; }
    .page__header { display: flex; justify-content: space-between; align-items: center; }
    .page__actions a { margin-inline-start: 8px; }
    .search { width: 100%; max-width: 400px; margin-block: 16px; display: block; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 16px; }
    .card { text-decoration: none; color: inherit; }
    img { width: 100%; height: 140px; object-fit: cover; border-radius: 6px 6px 0 0; }
    h3 { margin: 8px 0 4px; }
    .muted { color: var(--mat-sys-on-surface-variant); }
  `],
})
export class RecipesListComponent {
  private readonly svc = inject(RecipesService);
  readonly recipes = signal<RecipeSummary[]>([]);
  readonly search = signal('');
  private debounce?: ReturnType<typeof setTimeout>;

  constructor() { this.reload(); }

  onSearchChange(v: string) {
    this.search.set(v);
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => this.reload(), 250);
  }

  private async reload() {
    const items = await firstValueFrom(this.svc.list(this.search() || undefined));
    this.recipes.set(items);
  }
}
