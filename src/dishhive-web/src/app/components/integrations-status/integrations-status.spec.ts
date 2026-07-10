import { describe, expect, it } from 'vitest';
import { WebSearchIntegrationStatus } from '../../models/integration-status.model';
import { IntegrationsService } from '../../services/integrations.service';
import { IntegrationsStatusComponent } from './integrations-status';

describe('IntegrationsStatusComponent', () => {
  const component = new IntegrationsStatusComponent({} as IntegrationsService);

  it('labels a reachable but non-operational web search integration as misconfigured', () => {
    const status: WebSearchIntegrationStatus = {
      configured: true,
      reachable: true,
      operational: false,
      provider: 'searxng',
      baseUrl: 'http://searxng:8080',
      error: 'JSON search is forbidden'
    };

    expect(component.webSearchChipLabel(status)).toBe('Misconfigured');
    expect(component.webSearchChipIcon(status)).toBe('warning');
    expect(component.webSearchChipClass(status)).toBe('status-chip--warn');
  });

  it('labels an operational web search integration as active', () => {
    const status: WebSearchIntegrationStatus = {
      configured: true,
      reachable: true,
      operational: true,
      provider: 'searxng',
      baseUrl: 'http://searxng:8080',
      error: null
    };

    expect(component.webSearchChipLabel(status)).toBe('Active');
  });
});
