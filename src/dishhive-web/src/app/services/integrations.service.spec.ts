import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { IntegrationsService } from './integrations.service';

describe('IntegrationsService metrics queries', () => {
  let service: IntegrationsService;
  let httpTesting: HttpTestingController;
  const endpoint = '/api/integrations/ai/planning-runs';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(IntegrationsService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('omits unset filters from the all-tasks HTTP query', () => {
    service.getAiPlanningMetrics(30, {
      from: undefined, task: undefined, model: undefined,
      fingerprint: undefined, outcome: undefined, operationId: undefined
    }).subscribe();

    const request = httpTesting.expectOne(request => request.url.split('?')[0] === endpoint);
    request.flush(null);

    expect(request.request.urlWithParams).toBe(`${endpoint}?count=30`);
  });

  it('preserves every supplied filter and the requested count', () => {
    const filters = {
      from: '2026-11-12T12:00:00Z', task: 'recipe.facts', model: 'provider/model:variant',
      fingerprint: 'config|a&b+c', outcome: 'validationFailure', operationId: 'planning-123'
    };
    service.getAiPlanningMetrics(12, filters).subscribe();

    const request = httpTesting.expectOne(request => request.url.split('?')[0] === endpoint);
    request.flush(null);

    const params = new URL(request.request.urlWithParams, 'http://localhost').searchParams;
    expect(params.get('count')).toBe('12');
    for (const [name, value] of Object.entries(filters)) {
      expect(params.get(name)).toBe(value);
    }
  });
});
