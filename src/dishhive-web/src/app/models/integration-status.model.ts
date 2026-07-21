export interface AiIntegrationStatus {
  configured: boolean;
  reachable: boolean;
  provider: string | null;
  model: string | null;
  baseUrl: string | null;
  statsEnabled: boolean;
  /** Lifecycle of the model capability test (notConfigured | notRun | running | completed) */
  modelTestState: AiModelTestState;
  /** Headline of the last completed test (passed | warnings | failed), null before any run */
  modelTestVerdict: AiModelTestVerdict | null;
}

export type AiModelTestState = 'notConfigured' | 'notRun' | 'running' | 'completed';
export type AiModelTestVerdict = 'passed' | 'warnings' | 'failed';

export interface AiModelTestCheck {
  name: string;
  passed: boolean;
  detail: string;
}

export interface AiModelTestResult {
  testedAt: string;
  verdict: AiModelTestVerdict;
  responseMode: string;
  evaluationPassed: boolean | null;
  tokensPerSecond: number | null;
  elapsedMs: number;
  checks: AiModelTestCheck[];
}

export interface AiModelTestStatus {
  state: AiModelTestState;
  result: AiModelTestResult | null;
}

export interface AiPlanningMetricsSummary {
  runCount: number;
  successfulRuns: number;
  partialFallbackRuns: number;
  fallbackRuns: number;
  averageTotalDurationMs: number;
  averageCompletionDurationMs: number;
  p95TotalDurationMs: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalReasoningTokens: number;
  totalSearches: number;
  totalEmptySearches: number;
  totalRecipeResolutions: number;
  totalResolutionFailures: number;
  totalParseFailures: number;
}

export interface AiTaskSummary {
  task: string;
  runCount: number;
  successfulRuns: number;
  failedRuns: number;
  averageDurationMs: number;
  p95DurationMs: number;
  totalTokens: number;
}

export interface AiTaskMetrics {
  runCount: number;
  successfulRuns: number;
  failedRuns: number;
  averageDurationMs: number;
  p95DurationMs: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalReasoningTokens: number;
  tasks: AiTaskSummary[];
}

export interface AiTaskRun {
  id: string;
  operationId: string;
  task: string;
  provider: string;
  model: string;
  configFingerprint: string;
  contractVersion: string;
  startedAt: string;
  outcome: string;
  responseMode: string;
  finishReason?: string;
  error?: string;
  attempt: number;
  maxOutputTokens: number;
  temperature: number;
  reasoningDisabled: boolean;
  schemaRequested: boolean;
  schemaApplied: boolean;
  promptCharacters: number;
  outputCharacters: number;
  inputTokens: number;
  outputTokens: number;
  reasoningTokens: number;
  totalTokens: number;
  durationMs: number;
}

export interface AiPlanningRun {
  id: string;
  requestId: string;
  startedAt: string;
  outcome: string;
  provider: string;
  model: string;
  instructions?: string;
  normalizedIntentJson?: string;
  error?: string;
  requestedDays: number;
  suggestedItems: number;
  externalSuggestions: number;
  fallbackSuggestions: number;
  usedExternalResearch: boolean;
  completionAttempts: number;
  parseFailures: number;
  modelTurns: number;
  inputTokens: number;
  outputTokens: number;
  reasoningTokens: number;
  totalTokens: number;
  researchCalls: number;
  searchCount: number;
  emptySearchCount: number;
  searchResultCount: number;
  recipeResolutionCount: number;
  recipeResolutionFailureCount: number;
  capabilityWaitMs: number;
  completionDurationMs: number;
  searchDurationMs: number;
  recipeResolutionDurationMs: number;
  totalDurationMs: number;
}

export interface AiPlanningMetricsResponse {
  summary: AiPlanningMetricsSummary;
  runs: AiPlanningRun[];
  taskMetrics: AiTaskMetrics;
  calls: AiTaskRun[];
}

export interface FreezyIntegrationStatus {
  configured: boolean;
  reachable: boolean;
  baseUrl: string | null;
}

export interface ScraperIntegrationStatus {
  configured: boolean;
  reachable: boolean;
  baseUrl: string | null;
  packageVersion: string | null;
}

export interface ScraperVersionCheck {
  installedVersion: string;
  latestVersion: string | null;
  updateAvailable: boolean;
}

export interface ScraperUpdateResponse {
  targetVersion: string | null;
}

export interface WebSearchIntegrationStatus {
  configured: boolean;
  reachable: boolean;
  operational: boolean;
  provider: string | null;
  baseUrl: string | null;
  error: string | null;
}

export interface IntegrationStatusResponse {
  ai: AiIntegrationStatus;
  freezy: FreezyIntegrationStatus;
  scraper: ScraperIntegrationStatus;
  webSearch: WebSearchIntegrationStatus;
}
