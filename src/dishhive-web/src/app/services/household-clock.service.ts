import { DestroyRef, Injectable, NgZone, inject, signal } from '@angular/core';

export function householdDate(instant: Date, timeZone: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(instant);
  const value = (type: string) => parts.find(part => part.type === type)!.value;
  return `${value('year')}-${value('month')}-${value('day')}`;
}

@Injectable({ providedIn: 'root' })
export class HouseholdClockService {
  readonly timeZone = signal('Europe/Brussels');
  private readonly minute = signal(0);

  constructor() {
    const destroyRef = inject(DestroyRef);
    inject(NgZone).runOutsideAngular(() => {
      const timer = setInterval(() => this.minute.update(value => value + 1), 60_000);
      destroyRef.onDestroy(() => clearInterval(timer));
    });
  }

  today(): string {
    this.minute(); // Refresh visible day flags when an open app crosses midnight.
    return householdDate(new Date(), this.timeZone());
  }

  /** Calendar arithmetic uses local components of this date, never its UTC instant. */
  date(): Date {
    const [year, month, day] = this.today().split('-').map(Number);
    return new Date(year, month - 1, day);
  }
}
