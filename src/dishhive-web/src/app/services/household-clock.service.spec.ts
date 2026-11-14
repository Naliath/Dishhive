import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { HouseholdClockService, householdDate } from './household-clock.service';

describe('HouseholdClockService', () => {
  afterEach(() => vi.useRealTimers());

  it.each([
    ['2026-11-13T22:59:59Z', '2026-11-13'],
    ['2026-11-13T23:00:00Z', '2026-11-14'],
    ['2026-03-29T00:59:59Z', '2026-03-29'],
    ['2026-03-29T01:00:00Z', '2026-03-29'],
    ['2026-03-29T22:00:00Z', '2026-03-30'],
    ['2026-10-25T00:30:00Z', '2026-10-25'],
    ['2026-10-25T01:30:00Z', '2026-10-25'],
    ['2026-10-25T23:00:00Z', '2026-10-26']
  ])('matches server calendar boundaries at %s', (instant, expected) => {
    expect(householdDate(new Date(instant), 'Europe/Brussels')).toBe(expected);
  });

  it('refreshes an open app across midnight and follows the configured zone', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-13T22:59:30Z'));
    const clock = TestBed.inject(HouseholdClockService);
    expect(clock.today()).toBe('2026-11-13');
    vi.advanceTimersByTime(60_000);
    expect(clock.today()).toBe('2026-11-14');
    clock.timeZone.set('America/New_York');
    expect(clock.today()).toBe('2026-11-13');
  });
});
