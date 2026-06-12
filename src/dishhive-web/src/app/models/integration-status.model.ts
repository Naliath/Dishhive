export interface AiIntegrationStatus {
  configured: boolean;
  reachable: boolean;
  provider: string | null;
  model: string | null;
  baseUrl: string | null;
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

export interface IntegrationStatusResponse {
  ai: AiIntegrationStatus;
  freezy: FreezyIntegrationStatus;
  scraper: ScraperIntegrationStatus;
}
