import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { catchError, concatMap, defer, from, of, throwError, timeout } from 'rxjs';

const cacheName = 'dishhive-offline-reads-v1';
const maxAgeMs = 7 * 24 * 60 * 60 * 1000;
const cachedAtHeader = 'x-dishhive-saved-at';
const bodyKindHeader = 'x-dishhive-body-kind';

export function isOfflineRead(request: HttpRequest<unknown>): boolean {
  const path = new URL(request.url, document.baseURI).pathname.toLowerCase();
  return request.method === 'GET' && (
    /^\/api\/(plannedmeals|shoppinglist|familymembers|cookbooks|recipes|statistics)(\/|$)/.test(path) ||
    /^\/api\/settings\/(preferences|languages)$/.test(path));
}

/** Network-first reads with observable provenance; writes are never queued. */
@Injectable({ providedIn: 'root' })
export class OfflineReadCacheService {
  private revision = 0;
  private view = 0;
  get generation(): number { return this.revision; }
  get viewGeneration(): number { return this.view; }

  beginView(): void {
    this.view++;
    this.savedResponses.set({});
  }
  private readonly savedResponses = signal<Record<string, number>>({});
  readonly oldestSavedAt = computed(() => {
    const dates = Object.values(this.savedResponses());
    return dates.length ? new Date(Math.min(...dates)).toISOString() : null;
  });

  private key(request: HttpRequest<unknown>): string {
    return new URL(request.urlWithParams, document.baseURI).href;
  }

  async save(request: HttpRequest<unknown>, response: HttpResponse<unknown>, generation = this.revision): Promise<void> {
    if (generation !== this.revision) return;
    this.markFresh(this.key(request));
    try {
      if (typeof caches === 'undefined') return;
      const cache = await caches.open(cacheName);
      if (generation !== this.revision) return;
      const headers = new Headers({ [cachedAtHeader]: String(Date.now()) });
      const kind = response.body instanceof Blob ? 'blob' : typeof response.body === 'string' ? 'text' : 'json';
      headers.set(bodyKindHeader, kind);
      headers.set('Content-Type', response.headers.get('Content-Type') ?? 'application/json');
      const body = kind === 'json' ? JSON.stringify(response.body) : response.body as Blob | string;
      await cache.put(this.key(request), new Response(body, { status: 200, headers }));
      const keys = await cache.keys();
      for (const key of keys.slice(0, Math.max(0, keys.length - 300))) await cache.delete(key);
    } catch { /* Cache storage/quota failure must not fail an online read. */ }
  }

  async read(request: HttpRequest<unknown>, view = this.view): Promise<HttpResponse<unknown> | null> {
    try {
      if (typeof caches === 'undefined') return null;
      const cache = await caches.open(cacheName);
      const key = this.key(request);
      const response = await cache.match(key);
      if (!response) return null;
      const savedAt = Number(response.headers.get(cachedAtHeader));
      if (!Number.isFinite(savedAt) || savedAt <= 0 || Date.now() - savedAt > maxAgeMs) {
        await cache.delete(key);
        return null;
      }
      const kind = response.headers.get(bodyKindHeader);
      const body = kind === 'blob' ? await response.blob() : kind === 'text' ? await response.text() : await response.json();
      if (view === this.view) this.savedResponses.update(values => ({ ...values, [key]: savedAt }));
      return new HttpResponse({ body, status: 200, url: request.urlWithParams });
    } catch { return null; }
  }

  private markFresh(key: string): void {
    this.savedResponses.update(values => {
      const next = { ...values };
      delete next[key];
      return next;
    });
  }

  async invalidate(): Promise<void> {
    this.revision++;
    this.savedResponses.set({});
    try { if (typeof caches !== 'undefined') await caches.delete(cacheName); } catch { }
  }
}

export const offlineReadInterceptor: HttpInterceptorFn = (original, next) => {
  if (!new URL(original.url, document.baseURI).pathname.startsWith('/api/')) return next(original);
  const cache = inject(OfflineReadCacheService);
  // Prevent older Angular service workers from invisibly supplying cached API data.
  const request = original.clone({ setHeaders: { 'ngsw-bypass': 'true' } });
  const cacheable = isOfflineRead(request);
  const generation = cache.generation;
  const view = cache.viewGeneration;
  const mutation = !['GET', 'HEAD', 'OPTIONS'].includes(request.method);
  const network = next(request).pipe(concatMap(event => {
    if (!(event instanceof HttpResponse)) return of(event);
    return from((cacheable ? cache.save(request, event, generation) : mutation ? cache.invalidate() : Promise.resolve())
      .then(() => event));
  }));
  if (!cacheable) return network;
  return network.pipe(
    timeout({ each: 8000 }),
    catchError(error => {
      // Never hide permissions, deleted resources or validation problems behind old data.
      if (error instanceof HttpErrorResponse && error.status > 0 && error.status < 500) return throwError(() => error);
      return defer(() => cache.read(request, view)).pipe(concatMap(saved =>
        saved ? of(saved as HttpEvent<unknown>) : throwError(() => error)));
    }));
};
