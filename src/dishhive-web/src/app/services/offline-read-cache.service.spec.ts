import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OfflineReadCacheService, offlineReadInterceptor } from './offline-read-cache.service';

describe('observable offline reads', () => {
  let http: HttpClient;
  let requests: HttpTestingController;
  let cache: OfflineReadCacheService;
  const stored = new Map<string, Response>();
  beforeEach(() => {
    stored.clear();
    vi.stubGlobal('caches', {
      open: async () => ({
        put: async (key: string, response: Response) => { stored.set(key, response); },
        match: async (key: string) => stored.get(key)?.clone(),
        keys: async () => [...stored.keys()],
        delete: async (key: string) => stored.delete(key)
      }),
      delete: async () => { stored.clear(); return true; }
    });
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([offlineReadInterceptor])), provideHttpClientTesting()] });
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
    cache = TestBed.inject(OfflineReadCacheService);
  });
  afterEach(() => { requests.verify(); vi.unstubAllGlobals(); vi.useRealTimers(); });

  async function online(value: object) {
    const response = firstValueFrom(http.get('/api/shoppinglist?from=2026-11-13'));
    const request = requests.expectOne('/api/shoppinglist?from=2026-11-13');
    expect(request.request.headers.get('ngsw-bypass')).toBe('true');
    request.flush(value);
    return response;
  }

  it('uses saved data during an API outage even while the browser is online, then clears its provenance on fresh data', async () => {
    await online({ items: ['carrots'] });
    expect(cache.oldestSavedAt()).toBeNull();
    const fallback = firstValueFrom(http.get('/api/shoppinglist?from=2026-11-13'));
    requests.expectOne('/api/shoppinglist?from=2026-11-13').flush('unavailable', { status: 503, statusText: 'Unavailable' });
    expect(await fallback).toEqual({ items: ['carrots'] });
    expect(cache.oldestSavedAt()).not.toBeNull();
    expect(await online({ items: ['beans'] })).toEqual({ items: ['beans'] });
    expect(cache.oldestSavedAt()).toBeNull();
  });

  it('invalidates dependent reads after a successful write and never queues a failed write', async () => {
    await online({ items: ['old'] });
    const write = firstValueFrom(http.put('/api/plannedmeals/meal', {}));
    requests.expectOne('/api/plannedmeals/meal').flush({});
    await write;
    expect(stored.size).toBe(0);
    const failed = firstValueFrom(http.put('/api/plannedmeals/meal', {})).catch(error => error.status);
    requests.expectOne('/api/plannedmeals/meal').error(new ProgressEvent('offline'));
    expect(await failed).toBe(0);
    expect(stored.size).toBe(0);
  });

  it('does not conceal a deleted or forbidden resource behind a saved response', async () => {
    await online({ items: ['old'] });
    for (const status of [401, 403, 404]) {
      const result = firstValueFrom(http.get('/api/shoppinglist?from=2026-11-13')).catch(error => error.status);
      requests.expectOne('/api/shoppinglist?from=2026-11-13').flush({}, { status, statusText: 'Rejected' });
      expect(await result).toBe(status);
    }
    expect(cache.oldestSavedAt()).toBeNull();
  });

  it('does not repopulate invalidated caches when an older read completes after a write', async () => {
    const old = firstValueFrom(http.get('/api/shoppinglist?from=2026-11-13'));
    const pending = requests.expectOne('/api/shoppinglist?from=2026-11-13');
    const write = firstValueFrom(http.post('/api/plannedmeals', {}));
    requests.expectOne('/api/plannedmeals').flush({});
    await write;
    pending.flush({ items: ['old'] });
    await old;
    expect(stored.size).toBe(0);
    await online({ items: ['new'] });
    expect(stored.size).toBe(1);
  });

  it('expires saved data after seven days', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-13T12:00:00Z'));
    await online({ items: ['old'] });
    vi.setSystemTime(new Date('2026-11-21T12:00:00Z'));
    const result = firstValueFrom(http.get('/api/shoppinglist?from=2026-11-13')).catch(error => error.status);
    requests.expectOne('/api/shoppinglist?from=2026-11-13').error(new ProgressEvent('offline'));
    expect(await result).toBe(0);
    expect(stored.size).toBe(0);
  });

  it('does not carry a saved-data notice from a previous view into a fresh page', async () => {
    await online({ items: ['old'] });
    const old = firstValueFrom(http.get('/api/shoppinglist?from=2026-11-13'));
    const pending = requests.expectOne('/api/shoppinglist?from=2026-11-13');
    cache.beginView();
    pending.error(new ProgressEvent('offline'));
    await old;
    expect(cache.oldestSavedAt()).toBeNull();
    const current = firstValueFrom(http.get('/api/shoppinglist?from=2026-11-13'));
    requests.expectOne('/api/shoppinglist?from=2026-11-13').error(new ProgressEvent('offline'));
    await current;
    expect(cache.oldestSavedAt()).not.toBeNull();
    cache.beginView();
    expect(cache.oldestSavedAt()).toBeNull();
  });
});

