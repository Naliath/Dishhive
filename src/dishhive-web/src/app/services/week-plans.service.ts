import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { UpdateAttendees, UpdateMealSlot, WeekPlan } from '../models';

@Injectable({ providedIn: 'root' })
export class WeekPlansService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/week-plans';

  /** Loads (or auto-creates) the plan for the week containing the given date. */
  forWeek(weekStart: string): Observable<WeekPlan[]> {
    const params = new HttpParams().set('weekStart', weekStart);
    return this.http.get<WeekPlan[]>(this.base, { params });
  }

  list(): Observable<WeekPlan[]> {
    return this.http.get<WeekPlan[]>(this.base);
  }

  get(id: string): Observable<WeekPlan> {
    return this.http.get<WeekPlan>(`${this.base}/${id}`);
  }

  updateSlot(planId: string, slotId: string, dto: UpdateMealSlot): Observable<unknown> {
    return this.http.put(`${this.base}/${planId}/slots/${slotId}`, dto);
  }

  updateAttendees(planId: string, slotId: string, dto: UpdateAttendees): Observable<unknown> {
    return this.http.put(`${this.base}/${planId}/slots/${slotId}/attendees`, dto);
  }
}
