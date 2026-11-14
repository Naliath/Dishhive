export type AiIntegrationStatus = import('../api/generated/dishhive-api.client').AiIntegrationStatusDto;

export type AiModelTestState = 'notConfigured' | 'notRun' | 'running' | 'completed';
export type AiModelTestVerdict = 'passed' | 'warnings' | 'failed';

export type AiModelTestCheck = import('../api/generated/dishhive-api.client').AiModelTestCheckDto;

export type AiModelTestResult = import('../api/generated/dishhive-api.client').AiModelTestResultDto;

export type AiModelTestStatus = import('../api/generated/dishhive-api.client').AiModelTestStatusDto;

export type AiPlanningMetricsSummary = import('../api/generated/dishhive-api.client').AiPlanningMetricsDto;

export type AiTaskSummary = import('../api/generated/dishhive-api.client').AiTaskSummaryDto;

export type AiTaskMetrics = import('../api/generated/dishhive-api.client').AiTaskMetricsDto;

export type AiTaskRun = import('../api/generated/dishhive-api.client').AiTaskRunDto;

export type AiPlanningRun = import('../api/generated/dishhive-api.client').AiPlanningRunDto;

export type AiPlanningMetricsResponse = import('../api/generated/dishhive-api.client').AiPlanningMetricsResponseDto;

export type FreezyIntegrationStatus = import('../api/generated/dishhive-api.client').FreezyIntegrationStatusDto;

export type ScraperIntegrationStatus = import('../api/generated/dishhive-api.client').ScraperIntegrationStatusDto;

export type ScraperVersionCheck = import('../api/generated/dishhive-api.client').ScraperVersionCheckDto;

export type ScraperUpdateResponse = import('../api/generated/dishhive-api.client').ScraperUpdateResponseDto;

export type WebSearchIntegrationStatus = import('../api/generated/dishhive-api.client').WebSearchIntegrationStatusDto;

export type IntegrationStatusResponse = import('../api/generated/dishhive-api.client').IntegrationStatusResponseDto;
