import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { SESSION_DETAIL_API_ORIGIN } from '../session-detail/session-detail-api';
import { AuthService } from './auth.service';

// Adds the bearer token to requests for this application's API and to nothing else, and ends the session when the API says the
// token it received is no longer acceptable (the API, not the browser, decides whether a token is valid).
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const apiPrefix = `${inject(SESSION_DETAIL_API_ORIGIN)}/api/`;
  if (!request.url.startsWith(apiPrefix)) return next(request);
  const token = auth.accessToken();
  if (token === null) return next(request);
  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) auth.expire('rejected');
      return throwError(() => error);
    }),
  );
};
