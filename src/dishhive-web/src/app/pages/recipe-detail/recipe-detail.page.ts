import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RecipesService } from '../../services/recipes.service';
import { MeasurementService } from '../../services/measurement.service';
import { Recipe } from '../../models/recipe.model';

@Component({
  selector: 'app-recipe-detail-page',
  standalone: true,
  imports: [
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSnackBarModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipe-detail.page.html',
  styleUrl: './recipe-detail.page.scss'
})
export class RecipeDetailPage implements OnInit {
  readonly recipe = signal<Recipe | null>(null);
  readonly loading = signal(true);
  /** Show the verbatim source lines next to normalized values (imported recipes) */
  readonly showOriginal = signal(false);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private recipesService: RecipesService,
    private measurementService: MeasurementService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.router.navigate(['/recipes']);
      return;
    }

    this.recipesService.getRecipe(id).subscribe({
      next: recipe => {
        this.recipe.set(recipe);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Could not load the recipe', 'Dismiss', { duration: 4000 });
        this.router.navigate(['/recipes']);
      }
    });
  }

  remove(): void {
    const recipe = this.recipe();
    if (!recipe) {
      return;
    }

    this.recipesService.deleteRecipe(recipe.id).subscribe({
      next: () => {
        this.snackBar.open('Recipe deleted', 'Dismiss', { duration: 3000 });
        this.router.navigate(['/recipes']);
      },
      error: () => this.snackBar.open('Could not delete the recipe', 'Dismiss', { duration: 4000 })
    });
  }

  formatQuantity(quantity?: number, unit?: string): string {
    // Honors the household measurement preference (metric default)
    return this.measurementService.format(quantity, unit);
  }
}
