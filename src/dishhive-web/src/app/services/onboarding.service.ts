import { OnboardingClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { tap } from 'rxjs/operators';
import { OnboardingStatus } from '../models/onboarding.model';

/** Bump the suffix when a future release must offer onboarding again. */
export const ONBOARDING_RESOLVED_CACHE_KEY = 'dishhive.onboarding.resolved.v1';

@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private readonly api: OnboardingClient;
  private readonly apiUrl = '/api/onboarding';

  constructor(http: HttpClient) { this.api = new OnboardingClient(http, ""); }

  /**
   * Resolves synchronously from the browser cache after onboarding has been handled.
   * A cache miss asks the backend, which remains authoritative for new browsers and
   * cleared site data.
   */
  start(): Observable<OnboardingStatus> {
    if (this.isResolvedLocally()) {
      return of({ shouldShow: false });
    }

    return apiResponse(this.api.start()).pipe(
      tap(status => {
        if (!status.shouldShow) {
          this.markResolvedLocally();
        }
      })
    );
  }

  complete(skipped: boolean): Observable<OnboardingStatus> {
    return apiResponse(this.api.complete({ skipped })).pipe(
      tap(() => this.markResolvedLocally())
    );
  }

  private isResolvedLocally(): boolean {
    try {
      return typeof localStorage !== 'undefined'
        && localStorage.getItem(ONBOARDING_RESOLVED_CACHE_KEY) === 'true';
    } catch {
      // Storage can be unavailable in restricted/privacy browser contexts.
      return false;
    }
  }

  private markResolvedLocally(): void {
    try {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(ONBOARDING_RESOLVED_CACHE_KEY, 'true');
      }
    } catch {
      // The server marker still prevents the wizard from returning; only the fast
      // local shortcut is lost when storage is unavailable.
    }
  }
}
