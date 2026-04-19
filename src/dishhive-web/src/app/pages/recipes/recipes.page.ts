import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { RecipesService } from '../../services/recipes.service';
import { RecipeSummary } from '../../models/recipe.model';
import { ImportRecipeDialogComponent } from '../../components/import-recipe-dialog/import-recipe-dialog.component';
import { debounceTime, distinctUntilChanged, Subject, switchMap } from 'rxjs';

@Component({
  selector: 'app-recipes',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatProgressSpinnerModule,
    MatIconModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatInputModule,
    MatFormFieldModule,
    MatSnackBarModule,
    RouterLink,
  ],
  templateUrl: './recipes.page.html',
  styleUrl: './recipes.page.scss'
})
export class RecipesPage implements OnInit {
  recipes = signal<RecipeSummary[]>([]);
  loading = signal(true);
  searchQuery = '';

  private search$ = new Subject<string>();

  constructor(
    private recipesService: RecipesService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadRecipes();

    this.search$.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(q => this.recipesService.getAll(q || undefined))
    ).subscribe({
      next: (recipes) => { this.recipes.set(recipes); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  loadRecipes(): void {
    this.loading.set(true);
    this.recipesService.getAll(this.searchQuery || undefined).subscribe({
      next: (r) => { this.recipes.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  onSearchChange(query: string): void {
    this.loading.set(true);
    this.search$.next(query);
  }

  openImportDialog(): void {
    const dialogRef = this.dialog.open(ImportRecipeDialogComponent, { width: '480px' });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.snackBar.open('Recipe imported!', undefined, { duration: 2500 });
        this.loadRecipes();
      }
    });
  }

  deleteRecipe(recipe: RecipeSummary, event: Event): void {
    event.stopPropagation();
    if (!confirm(`Delete "${recipe.title}"?`)) return;
    this.recipesService.delete(recipe.id).subscribe({
      next: () => {
        this.recipes.update(list => list.filter(r => r.id !== recipe.id));
        this.snackBar.open('Recipe deleted', undefined, { duration: 2000 });
      },
      error: () => this.snackBar.open('Failed to delete recipe', 'Dismiss', { duration: 3000 })
    });
  }
}
