import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatChipsModule } from '@angular/material/chips';
import { firstValueFrom } from 'rxjs';
import { RecipesService } from '../../services/recipes.service';
import { CreateRecipe, Recipe } from '../../models';

@Component({
  selector: 'app-recipe-edit',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule,
    MatFormFieldModule, MatInputModule, MatIconModule, MatChipsModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page__header">
        <h1>{{ id() ? 'Edit recipe' : 'New recipe' }}</h1>
        <a mat-stroked-button routerLink="/recipes">Back</a>
      </header>

      <mat-card>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full">
            <mat-label>Title</mat-label>
            <input matInput [(ngModel)]="form.title" />
          </mat-form-field>
          <mat-form-field appearance="outline" class="full">
            <mat-label>Description</mat-label>
            <textarea matInput rows="3" [(ngModel)]="form.description"></textarea>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Servings</mat-label>
            <input matInput type="number" [(ngModel)]="form.servings" />
          </mat-form-field>
          <mat-form-field appearance="outline" class="full">
            <mat-label>Image URL</mat-label>
            <input matInput [(ngModel)]="form.imageUrl" />
          </mat-form-field>

          <h3>Ingredients</h3>
          @for (i of form.ingredients; track $index) {
            <div class="row">
              <mat-form-field appearance="outline"><mat-label>Qty</mat-label>
                <input matInput type="number" [(ngModel)]="i.quantity" /></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Unit</mat-label>
                <input matInput [(ngModel)]="i.unit" /></mat-form-field>
              <mat-form-field appearance="outline" class="grow"><mat-label>Name</mat-label>
                <input matInput [(ngModel)]="i.name" /></mat-form-field>
              <button mat-icon-button (click)="removeIngredient($index)"><mat-icon>close</mat-icon></button>
            </div>
          }
          <button mat-stroked-button (click)="addIngredient()"><mat-icon>add</mat-icon> Add ingredient</button>

          <h3>Steps</h3>
          @for (s of form.steps; track $index) {
            <div class="row">
              <span class="step-num">{{ $index + 1 }}.</span>
              <mat-form-field appearance="outline" class="grow"><mat-label>Instruction</mat-label>
                <textarea matInput rows="2" [(ngModel)]="s.text"></textarea></mat-form-field>
              <button mat-icon-button (click)="removeStep($index)"><mat-icon>close</mat-icon></button>
            </div>
          }
          <button mat-stroked-button (click)="addStep()"><mat-icon>add</mat-icon> Add step</button>

          <h3>Tags</h3>
          <mat-form-field appearance="outline" class="full">
            <mat-label>Comma-separated tags</mat-label>
            <input matInput [ngModel]="tagsCsv()" (ngModelChange)="setTagsCsv($event)" />
          </mat-form-field>
        </mat-card-content>
        <mat-card-actions>
          <button mat-raised-button color="primary" (click)="save()">Save</button>
          @if (id()) {
            <button mat-button color="warn" (click)="remove()">Delete</button>
          }
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styles: [`
    .page { padding: 16px; max-width: 800px; margin: 0 auto; }
    .page__header { display: flex; justify-content: space-between; align-items: center; }
    .full { width: 100%; }
    .row { display: flex; gap: 8px; align-items: center; }
    .grow { flex: 1; }
    .step-num { font-weight: 600; min-width: 24px; }
  `],
})
export class RecipeEditComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly svc = inject(RecipesService);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly id = signal<string | null>(null);

  form: CreateRecipe = this.empty();

  constructor() {
    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam && idParam !== 'new') {
      this.id.set(idParam);
      // OnPush + plain field mutation requires an explicit markForCheck so the
      // populated form values are rendered after the async load completes.
      this.svc.get(idParam).subscribe((r) => {
        this.fromRecipe(r);
        this.cdr.markForCheck();
      });
    }
  }

  private empty(): CreateRecipe {
    return {
      title: '',
      description: null,
      servings: 4,
      imageUrl: null,
      videoUrl: null,
      sourceUrl: null,
      sourceProviderKey: null,
      sourceRawPayload: null,
      notes: null,
      ingredients: [],
      steps: [],
      tags: [],
    };
  }

  private fromRecipe(r: Recipe) {
    this.form = {
      title: r.title,
      description: r.description ?? null,
      servings: r.servings,
      imageUrl: r.imageUrl ?? null,
      videoUrl: r.videoUrl ?? null,
      sourceUrl: r.sourceUrl ?? null,
      sourceProviderKey: r.sourceProviderKey ?? null,
      sourceRawPayload: null,
      notes: r.notes ?? null,
      ingredients: r.ingredients.map((i) => ({
        order: i.order, name: i.name, quantity: i.quantity ?? null, unit: i.unit ?? null,
      })),
      steps: r.steps.map((s) => ({ order: s.order, text: s.text })),
      tags: [...r.tags],
    };
  }

  tagsCsv = computed(() => this.form.tags.join(', '));
  setTagsCsv(v: string) {
    this.form.tags = v.split(',').map((t) => t.trim()).filter(Boolean);
  }

  addIngredient() {
    this.form.ingredients = [...this.form.ingredients, { order: this.form.ingredients.length, name: '', quantity: null, unit: null }];
  }
  removeIngredient(i: number) {
    this.form.ingredients = this.form.ingredients.filter((_, idx) => idx !== i);
  }
  addStep() {
    this.form.steps = [...this.form.steps, { order: this.form.steps.length, text: '' }];
  }
  removeStep(i: number) {
    this.form.steps = this.form.steps.filter((_, idx) => idx !== i);
  }

  async save() {
    this.form.ingredients.forEach((ing, idx) => (ing.order = idx));
    this.form.steps.forEach((s, idx) => (s.order = idx));
    if (this.id()) {
      await firstValueFrom(this.svc.update(this.id()!, this.form));
    } else {
      const created = await firstValueFrom(this.svc.create(this.form));
      this.id.set(created.id);
    }
    this.router.navigateByUrl('/recipes');
  }

  async remove() {
    if (!this.id()) return;
    if (!confirm('Delete this recipe?')) return;
    await firstValueFrom(this.svc.remove(this.id()!));
    this.router.navigateByUrl('/recipes');
  }
}
