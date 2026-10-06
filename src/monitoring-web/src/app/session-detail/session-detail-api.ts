import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable, InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';

export interface SessionDetail {
  eventId: string;
  siteId: string;
  sensorId: string;
  occurredAt: string;
  data: unknown;
}

export const SESSION_DETAIL_API_ORIGIN = new InjectionToken<string>('SESSION_DETAIL_API_ORIGIN', {
  factory: () => '',
  providedIn: 'root',
});

export function sessionDetailUrl(origin: string, eventId: string): string {
  return `${origin}/api/v1/sessions/${encodeURIComponent(eventId)}`;
}

@Injectable({ providedIn: 'root' })
export class SessionDetailApi {
  private readonly http = inject(HttpClient);
  private readonly origin = inject(SESSION_DETAIL_API_ORIGIN);

  get(eventId: string, scope?: { siteId: string; sensorId: string }): Observable<SessionDetail> {
    const params = scope ? new HttpParams().set('siteId', scope.siteId).set('sensorId', scope.sensorId) : new HttpParams();
    return this.http.get<SessionDetail>(sessionDetailUrl(this.origin, eventId), { params });
  }
}
