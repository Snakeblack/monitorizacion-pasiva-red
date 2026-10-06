import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { SESSION_DETAIL_API_ORIGIN } from '../session-detail/session-detail-api';
import { SessionFilters } from './session-query';

export interface SessionSummary {
  eventId: string;
  siteId: string;
  sensorId: string;
  sourceIp: string;
  destinationIp: string;
  sourcePort: number;
  destinationPort: number;
  protocol: 'TCP' | 'UDP';
  startedAt: string;
  endedAt: string;
  provenance: 'synthetic' | 'capture';
  inferred?: boolean | null;
  partial?: boolean | null;
}

export interface SessionPage {
  items: SessionSummary[];
  nextCursor?: string | null;
  freshness: {
    state: 'current' | 'lagging' | 'recovering';
    measuredAt: string;
    lagSeconds?: number | null;
  };
}

@Injectable({ providedIn: 'root' })
export class SessionSearchApi {
  private readonly http = inject(HttpClient);
  private readonly origin = inject(SESSION_DETAIL_API_ORIGIN);

  search(filters: SessionFilters, cursor = ''): Observable<SessionPage> {
    let params = new HttpParams()
      .set('from', filters.from)
      .set('to', filters.to)
      .set('pageSize', filters.pageSize);
    for (const key of [
      'siteId',
      'sensorId',
      'sourceIp',
      'destinationIp',
      'protocol',
      'sourcePort',
      'destinationPort',
    ] as const) {
      if (filters[key] !== '') params = params.set(key, filters[key]);
    }
    if (cursor) params = params.set('cursor', cursor);
    return this.http.get<SessionPage>(`${this.origin}/api/v1/sessions`, { params });
  }
}
