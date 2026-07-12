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
  fallbackRuns: number;
  averageTotalDurationMs: number;
  averageCompletionDurationMs: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalReasoningTokens: number;
  totalSearches: number;
  totalEmptySearches: number;
  totalRecipeResolutions: number;
  totalResolutionFailures: number;
  totalParseFailures: number;
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
