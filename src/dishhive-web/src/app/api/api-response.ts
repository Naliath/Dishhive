import { HttpErrorResponse } from '@angular/common/http';
import { catchError, Observable, throwError } from 'rxjs';
import { ApiException } from './generated/dishhive-api.client';

/** Keep status, field errors and the server's problem details across generated clients. */
export function apiResponse<T>(response: Observable<T>): Observable<T> {
  return response.pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse) return throwError(() => error);
    let problem: unknown = error;
    let status = 0;
    if (error instanceof ApiException) {
      status = error.status;
      try { problem = JSON.parse(error.response); } catch { problem = { title: error.message }; }
    }
    if (problem && typeof problem === 'object' && 'status' in problem && typeof problem.status === 'number') {
      status = problem.status;
    }
    return throwError(() => new HttpErrorResponse({ status, error: problem }));
  }));
}
