import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AiPlanningMetricsResponse } from '../../models/integration-status.model';
import { IntegrationsService } from '../../services/integrations.service';
import { AiPlanningStatsPage } from './ai-planning-stats.page';

describe('AiPlanningStatsPage', () => {
  const metrics = {
    summary: { runCount: 0 },
    runs: [],
    taskMetrics: { runCount: 2, tasks: [] },
    calls: [
      { id: '1', task: 'recipe.facts', outcome: 'success' },
      { id: '2', task: 'recipe.translate', outcome: 'validationFailure' }
    ]
  } as unknown as AiPlanningMetricsResponse;

  it('loads task metrics and filters recent calls by task', () => {
    const service = { getAiPlanningMetrics: vi.fn(() => of(metrics)) };
    const page = new AiPlanningStatsPage(service as unknown as IntegrationsService);

    page.refresh();
    page.selectTask('recipe.translate');

    expect(page.metrics()).toBe(metrics);
    expect(page.visibleCalls().map(call => call.id)).toEqual(['2']);
    expect(page.isFailure(page.visibleCalls()[0].outcome)).toBe(true);
  });

  it('clears stale metrics and exposes a refresh failure', () => {
    const service = { getAiPlanningMetrics: vi.fn(() => throwError(() => new Error('offline'))) };
    const page = new AiPlanningStatsPage(service as unknown as IntegrationsService);
    page.metrics.set(metrics);

    page.refresh();

    expect(page.metrics()).toBeNull();
    expect(page.failed()).toBe(true);
  });

  it('provides human-readable labels for every registered task and outcome', () => {
    const page = new AiPlanningStatsPage({} as IntegrationsService);

    expect(page.taskLabel('recipe.import_extract')).toBe('aiStats.tasks.recipeImportExtraction');
    expect(page.outcomeLabel('parseFailure')).toBe('aiStats.outcomes.parseFailure');
    expect(page.taskLabel('future.task')).toBe('future.task');
  });
});
