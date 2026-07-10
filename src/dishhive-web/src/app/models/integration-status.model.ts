export interface AiIntegrationStatus {
  configured: boolean;
  reachable: boolean;
  provider: string | null;
  model: string | null;
  baseUrl: string | null;
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
