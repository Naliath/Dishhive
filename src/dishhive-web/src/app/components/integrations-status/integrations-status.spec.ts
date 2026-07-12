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

    expect(component.webSearchChipLabel(status)).toBe('common.misconfigured');
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

    expect(component.webSearchChipLabel(status)).toBe('common.active');
  });

  it('presents model checks as product features without exposing test fixtures', () => {
    const feature = component.aiFeature({
      name: 'Specific dish',
      passed: true,
      detail: "'Chicken curry' was planned on Thursday as instructed"
    });

    expect(feature).toEqual({
      name: 'integration.specificMealInstructions',
      detail: 'integration.theModelCanPlaceARequestedMealOnTheRequestedDay'
    });
    expect(JSON.stringify(feature)).not.toContain('Chicken curry');
  });

  it('explains unavailable external recipe discovery as a feature limitation', () => {
    expect(component.aiFeature({
      name: 'External recipe intent',
      passed: false,
      detail: 'Diagnostic research-intent evidence'
    })).toEqual({
      name: 'integration.externalRecipeDiscovery',
      detail: 'integration.theModelCannotReliablyInterpretExternalRecipeRequests'
    });
  });

  it('explains that multilingual planning tests structured source requests, not fluency', () => {
    expect(component.aiFeature({
      name: 'Multilingual planning instructions',
      passed: false,
      detail: 'Raw diagnostic detail'
    })).toEqual({
      name: 'integration.multilingualSourceRequests',
      detail: 'integration.atLeastOneEnglishOrDutchSourceRequestDidNotProduceAUsableCountDateAndCoursePlanThisIsStricterThanUnderstandingConversationalLanguage'
    });
  });
});
