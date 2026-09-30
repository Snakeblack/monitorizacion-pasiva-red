import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SessionDetailApi } from './session-detail-api';

describe('SessionDetailApi', () => {
  let api: SessionDetailApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), SessionDetailApi],
    });
    api = TestBed.inject(SessionDetailApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('GETs /api/v1/sessions/{eventId} without scope headers or query', async () => {
    const pending = firstValueFrom(api.get('evt/1'));

    const req = http.expectOne('/api/v1/sessions/evt%2F1');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys()).toEqual([]);
    expect(req.request.headers.has('X-Site-Id')).toBe(false);
    expect(req.request.headers.has('X-Sensor-Id')).toBe(false);
    expect(new URL(req.request.urlWithParams, 'http://localhost').search).toBe('');

    const body = {
      eventId: 'evt/1',
      siteId: 'site-a',
      sensorId: 'sensor-a',
      occurredAt: '2026-09-29T12:00:00.100Z',
      data: { protocol: 'TCP', sourceIp: '192.0.2.1' },
    };
    req.flush(body);

    expect(await pending).toEqual(body);
  });

  it('GETs a second eventId at /api/v1/sessions/{id} with an empty origin', async () => {
    const pending = firstValueFrom(api.get('shared'));

    const req = http.expectOne('/api/v1/sessions/shared');
    expect(req.request.method).toBe('GET');
    expect(req.request.headers.has('X-Site-Id')).toBe(false);
    expect(req.request.headers.has('X-Sensor-Id')).toBe(false);

    const body = {
      eventId: 'shared',
      siteId: 'site-b',
      sensorId: 'sensor-a',
      occurredAt: '2026-09-29T12:00:00Z',
      data: { protocol: 'UDP' },
    };
    req.flush(body);

    expect(await pending).toEqual(body);
  });
});
