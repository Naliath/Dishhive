import { StatisticsClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { DishStatistics, MemberStatistics } from '../models/statistics.model';

@Injectable({ providedIn: 'root' })
export class StatisticsService {
  private readonly api: StatisticsClient;

  private apiUrl = '/api/statistics';

  constructor(http: HttpClient) {
    this.api = new StatisticsClient(http, "");
  }

  getDishStatistics(from?: string, to?: string): Observable<DishStatistics> {
    return apiResponse(this.api.getDishStatistics(from, to));
  }

  getMemberStatistics(memberId: string): Observable<MemberStatistics> {
    return apiResponse(this.api.getMemberStatistics(memberId));
  }

}
