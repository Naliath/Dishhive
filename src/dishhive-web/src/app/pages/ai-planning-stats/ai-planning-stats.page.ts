import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AiPlanningMetricsResponse } from '../../models/integration-status.model';
import { IntegrationsService } from '../../services/integrations.service';
import { TranslatePipe } from '../../services/language.service';

@Component({
  selector: 'app-ai-planning-stats-page',
  standalone: true,
  imports: [DatePipe, DecimalPipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, TranslatePipe],
  templateUrl: './ai-planning-stats.page.html',
  styleUrl: './ai-planning-stats.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AiPlanningStatsPage implements OnInit, OnDestroy {
  readonly metrics = signal<AiPlanningMetricsResponse | null>(null);
  readonly failed = signal(false);
  readonly selectedTask = signal<string | null>(null);
  readonly taskOptions = signal<AiPlanningMetricsResponse['taskMetrics']['tasks']>([]);
  private refreshSubscription?: Subscription;

  constructor(private integrationsService: IntegrationsService) {}

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.refreshSubscription?.unsubscribe();
    this.failed.set(false);
    this.metrics.set(null);
    this.refreshSubscription = this.integrationsService.getAiPlanningMetrics(30, {
      task: this.selectedTask() ?? undefined
    }).subscribe({
      next: result => {
        this.metrics.set(result);
        if (this.selectedTask() === null) this.taskOptions.set(result.taskMetrics.tasks);
      },
      error: () => this.failed.set(true)
    });
  }

  ngOnDestroy(): void {
    this.refreshSubscription?.unsubscribe();
  }

  seconds(milliseconds: number): string {
    return `${(milliseconds / 1000).toFixed(1)}s`;
  }

  percent(successful: number, total: number): string {
    return total === 0 ? '0%' : `${Math.round(successful / total * 100)}%`;
  }

  selectTask(task: string | null): void {
    this.selectedTask.set(task);
    this.refresh();
  }

  isFailure(outcome: string): boolean {
    return !['success', 'noResult', 'cancelled', 'responseReceived', 'abandoned', 'completed']
      .includes(outcome);
  }

  taskLabel(task: string): string {
    return ({
      'capability.planning': 'aiStats.tasks.capabilityPlanning',
      'planning.intent': 'aiStats.tasks.planningIntent',
      'planning.research': 'aiStats.tasks.planningResearch',
      'planning.generate': 'aiStats.tasks.weekPlanning',
      'recipe.facts': 'aiStats.tasks.recipeFacts',
      'recipe.dish_match': 'aiStats.tasks.recipeDishMatch',
      'recipe.edit': 'aiStats.tasks.recipeEdit',
      'recipe.translate': 'aiStats.tasks.recipeTranslation',
      'recipe.import_extract': 'aiStats.tasks.recipeImportExtraction'
    } as Record<string, string>)[task] ?? task;
  }

  outcomeLabel(outcome: string): string {
    return ({
      accepted: 'aiStats.reviewOutcomes.accepted',
      rejected: 'aiStats.reviewOutcomes.rejected',
      needsReview: 'aiStats.reviewOutcomes.needsReview',
      stockUnavailable: 'aiStats.reviewOutcomes.stockUnavailable',
      importFailed: 'aiStats.reviewOutcomes.importFailed',
      planningFailed: 'aiStats.reviewOutcomes.planningFailed',
      success: 'aiStats.outcomes.success',
      noResult: 'aiStats.outcomes.noResult',
      parseFailure: 'aiStats.outcomes.parseFailure',
      validationFailure: 'aiStats.outcomes.validationFailure',
      timeout: 'aiStats.outcomes.timeout',
      cancelled: 'aiStats.outcomes.cancelled',
      error: 'aiStats.outcomes.error',
      inputLimit: 'aiStats.outcomes.inputLimit',
      rateLimited: 'aiStats.outcomes.rateLimited',
      budgetExceeded: 'aiStats.outcomes.budgetExceeded',
      responseReceived: 'aiStats.outcomes.incomplete',
      abandoned: 'aiStats.outcomes.abandoned',
      completed: 'aiStats.outcomes.incomplete'
    } as Record<string, string>)[outcome] ?? outcome;
  }
}
