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

export interface IntegrationStatusResponse {
  ai: AiIntegrationStatus;
  freezy: FreezyIntegrationStatus;
}
