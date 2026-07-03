import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { CookingLoaderComponent } from '../cooking-loader/cooking-loader';
import { RecipesService } from '../../services/recipes.service';
import { RecipeFactsStatus } from '../../models/recipe.model';

/**
 * Settings card for recipe dietary facts: shows how much of the library has been
 * assessed (unassessed / AI-detected / user-confirmed) and offers the backfill
 * button that queues all still-unassessed recipes. While the background queue
 * works, the card polls the status endpoint and shows the remaining count
 * (same pattern as the AI model re-test in integrations-status).
 */
@Component({
  selector: 'app-recipe-facts-settings',
  standalone: true,
  imports: [CookingLoaderComponent, MatButtonModule, MatCardModule, MatIconModule, MatSnackBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipe-facts-settings.html',
  styleUrl: './recipe-facts-settings.scss'
})
export class RecipeFactsSettingsComponent implements OnInit, OnDestroy {
  private static readonly pollIntervalMs = 2000;

  readonly status = signal<RecipeFactsStatus | null>(null);
  readonly starting = signal(false);

  /** The queue is working (poll + progress display active) */
  readonly running = computed(() => this.status()?.running ?? false);

  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(private recipesService: RecipesService, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.refresh();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  backfill(): void {
    this.starting.set(true);
    this.recipesService.backfillFacts().subscribe({
      next: result => {
        this.starting.set(false);
        if (result.enqueued === 0) {
          this.snackBar.open('Nothing to assess — every recipe already has facts', 'Dismiss', { duration: 4000 });
          return;
        }
        this.snackBar.open(`Assessing ${result.enqueued} recipes in the background`, 'Dismiss', { duration: 4000 });
        this.refresh();
      },
      error: () => {
        this.starting.set(false);
        this.snackBar.open('Could not start the assessment', 'Dismiss', { duration: 4000 });
      }
    });
  }

  private refresh(): void {
    this.recipesService.getFactsStatus().subscribe({
      next: status => {
        this.status.set(status);
        if (status.running) {
          this.schedulePoll();
        } else {
          this.stopPolling();
        }
      },
      error: () => this.stopPolling()
    });
  }

  private schedulePoll(): void {
    this.stopPolling();
    this.pollTimer = setTimeout(() => this.refresh(), RecipeFactsSettingsComponent.pollIntervalMs);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
