import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ONBOARDING_RESOLVED_CACHE_KEY, OnboardingService } from './onboarding.service';

describe('OnboardingService', () => {
  let service: OnboardingService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem(ONBOARDING_RESOLVED_CACHE_KEY);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(OnboardingService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.removeItem(ONBOARDING_RESOLVED_CACHE_KEY);
  });

  it('returns synchronously without an HTTP request when locally resolved', () => {
    localStorage.setItem(ONBOARDING_RESOLVED_CACHE_KEY, 'true');
    let shouldShow: boolean | undefined;

    service.start().subscribe(status => shouldShow = status.shouldShow);

    expect(shouldShow).toBe(false);
    httpTesting.expectNone('/api/onboarding/start');
  });

  it('caches a backend response that says onboarding is not required', () => {
    service.start().subscribe();

    httpTesting.expectOne('/api/onboarding/start').flush({ shouldShow: false });

    expect(localStorage.getItem(ONBOARDING_RESOLVED_CACHE_KEY)).toBe('true');
  });

  it('does not cache while onboarding is still required', () => {
    service.start().subscribe();

    httpTesting.expectOne('/api/onboarding/start').flush({ shouldShow: true });

    expect(localStorage.getItem(ONBOARDING_RESOLVED_CACHE_KEY)).toBeNull();
  });

  it.each([false, true])('caches a successful completion with skipped=%s', skipped => {
    service.complete(skipped).subscribe();

    const request = httpTesting.expectOne('/api/onboarding/complete');
    expect(request.request.body).toEqual({ skipped });
    request.flush({ shouldShow: false });

    expect(localStorage.getItem(ONBOARDING_RESOLVED_CACHE_KEY)).toBe('true');
  });
});
