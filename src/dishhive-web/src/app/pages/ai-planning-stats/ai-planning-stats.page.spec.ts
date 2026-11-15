import { of, Subject, throwError } from 'rxjs';
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

  it('keeps the selected task when older requests finish or fail later', () => {
    const oldRequest = new Subject<AiPlanningMetricsResponse>();
    const currentRequest = new Subject<AiPlanningMetricsResponse>();
    const service = { getAiPlanningMetrics: vi.fn()
      .mockReturnValueOnce(oldRequest).mockReturnValueOnce(currentRequest) };
    const page = new AiPlanningStatsPage(service as unknown as IntegrationsService);
    page.refresh();
    page.selectTask('recipe.translate');
    const current = { ...metrics, calls: [metrics.calls[1]] };
    currentRequest.next(current);
    oldRequest.next(metrics);
    oldRequest.error(new Error('late failure'));
    expect(page.metrics()).toBe(current);
    expect(page.failed()).toBe(false);
    page.ngOnDestroy();
  });

  it('requests task-filtered calls from the server', () => {
    const service = { getAiPlanningMetrics: vi.fn((_count: number, filters?: { task?: string }) => of({
      ...metrics,
      calls: filters?.task ? metrics.calls.filter(call => call.task === filters.task) : metrics.calls
    })) };
    const page = new AiPlanningStatsPage(service as unknown as IntegrationsService);

    page.refresh();
    page.selectTask('recipe.translate');

    expect(service.getAiPlanningMetrics).toHaveBeenLastCalledWith(30, { task: 'recipe.translate' });
    expect(page.metrics()!.calls.map(call => call.id)).toEqual(['2']);
    expect(page.isFailure(page.metrics()!.calls[0].outcome)).toBe(true);
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
    expect(page.taskLabel('recipe.import_text')).toBe('aiStats.tasks.recipeTextExtraction');
    expect(page.taskLabel('recipe.import_image')).toBe('aiStats.tasks.recipeImageExtraction');
    expect(page.taskLabel('capability.vision')).toBe('aiStats.tasks.visionCapability');
    expect(page.taskLabel('planning.research')).toBe('aiStats.tasks.planningResearch');
    expect(page.taskLabel('recipe.dish_match')).toBe('aiStats.tasks.recipeDishMatch');
    expect(page.outcomeLabel('parseFailure')).toBe('aiStats.outcomes.parseFailure');
    expect(page.outcomeLabel('rateLimited')).toBe('aiStats.outcomes.rateLimited');
    expect(page.outcomeLabel('budgetExceeded')).toBe('aiStats.outcomes.budgetExceeded');
    expect(page.isFailure('responseReceived')).toBe(false);
    expect(page.taskLabel('future.task')).toBe('future.task');
  });
});
