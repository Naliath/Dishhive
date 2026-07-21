import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
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
export class AiPlanningStatsPage implements OnInit {
  readonly metrics = signal<AiPlanningMetricsResponse | null>(null);
  readonly failed = signal(false);
  readonly selectedTask = signal<string | null>(null);
  readonly visibleCalls = computed(() => {
    const data = this.metrics();
    const task = this.selectedTask();
    return data?.calls.filter(call => task === null || call.task === task) ?? [];
  });

  constructor(private integrationsService: IntegrationsService) {}

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.failed.set(false);
    this.metrics.set(null);
    this.integrationsService.getAiPlanningMetrics().subscribe({
      next: result => this.metrics.set(result),
      error: () => this.failed.set(true)
    });
  }

  seconds(milliseconds: number): string {
    return `${(milliseconds / 1000).toFixed(1)}s`;
  }

  percent(successful: number, total: number): string {
    return total === 0 ? '0%' : `${Math.round(successful / total * 100)}%`;
  }

  selectTask(task: string | null): void {
    this.selectedTask.set(task);
  }

  isFailure(outcome: string): boolean {
    return outcome !== 'success' && outcome !== 'noResult';
  }

  taskLabel(task: string): string {
    return ({
      'capability.planning': 'aiStats.tasks.capabilityPlanning',
      'planning.intent': 'aiStats.tasks.planningIntent',
      'planning.generate': 'aiStats.tasks.weekPlanning',
      'recipe.facts': 'aiStats.tasks.recipeFacts',
      'recipe.translate': 'aiStats.tasks.recipeTranslation',
      'recipe.import_extract': 'aiStats.tasks.recipeImportExtraction'
    } as Record<string, string>)[task] ?? task;
  }

  outcomeLabel(outcome: string): string {
    return ({
      success: 'aiStats.outcomes.success',
      noResult: 'aiStats.outcomes.noResult',
      parseFailure: 'aiStats.outcomes.parseFailure',
      validationFailure: 'aiStats.outcomes.validationFailure',
      timeout: 'aiStats.outcomes.timeout',
      cancelled: 'aiStats.outcomes.cancelled',
      error: 'aiStats.outcomes.error',
      completed: 'aiStats.outcomes.incomplete'
    } as Record<string, string>)[outcome] ?? outcome;
  }
}
