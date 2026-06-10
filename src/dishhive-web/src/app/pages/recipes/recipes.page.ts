import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RecipesService } from '../../services/recipes.service';
import { RecipeListItem } from '../../models/recipe.model';

@Component({
  selector: 'app-recipes-page',
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
  templateUrl: './recipes.page.html',
  styleUrl: './recipes.page.scss'
})
export class RecipesPage implements OnInit {
  readonly recipes = signal<RecipeListItem[]>([]);
  readonly loading = signal(true);
  readonly importing = signal(false);
  readonly importVisible = signal(false);

  searchTerm = '';
  importUrl = '';

  constructor(
    private recipesService: RecipesService,
    private snackBar: MatSnackBar,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadRecipes();
  }

  loadRecipes(): void {
    this.loading.set(true);
    this.recipesService.getRecipes(this.searchTerm.trim() || undefined).subscribe({
      next: recipes => {
        this.recipes.set(recipes);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load recipes', 'Dismiss', { duration: 4000 });
      }
    });
  }

  toggleImport(): void {
    this.importVisible.update(visible => !visible);
  }

  import(): void {
    const url = this.importUrl.trim();
    if (!url) {
      return;
    }

    this.importing.set(true);
    this.recipesService.importRecipe(url).subscribe({
      next: recipe => {
        this.importing.set(false);
        this.importUrl = '';
        this.importVisible.set(false);
        this.snackBar.open(`Imported "${recipe.title}" — check the values`, 'Dismiss', { duration: 5000 });
        this.router.navigate(['/recipes', recipe.id]);
      },
      error: (error: HttpErrorResponse) => {
        this.importing.set(false);
        const detail = error.error?.title ?? 'Import failed';
        this.snackBar.open(detail, 'Dismiss', { duration: 5000 });
      }
    });
  }
}
