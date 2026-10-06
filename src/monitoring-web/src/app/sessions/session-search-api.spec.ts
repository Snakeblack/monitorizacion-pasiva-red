import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { recentSessionFilters } from './session-query';
import { SessionSearchApi } from './session-search-api';

describe('SessionSearchApi', () => {
  it('sends exact AND filters with UTC, bounded page, opaque cursor and no trust headers', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const api = TestBed.inject(SessionSearchApi);
    const http = TestBed.inject(HttpTestingController);
    const filters = {
      ...recentSessionFilters(Date.parse('2026-10-05T12:00:00Z')),
      siteId: 'Madrid/1',
      sensorId: 's2',
      sourceIp: '2001:db8::1',
      destinationIp: '192.0.2.2',
      protocol: 'TCP',
      sourcePort: '0',
      destinationPort: '65535',
      pageSize: 100,
    };
    const result = firstValueFrom(api.search(filters, 'opaque/+='));
    const req = http.expectOne((request) => request.url === '/api/v1/sessions');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-10-04T12:00:00.000Z');
    expect(req.request.params.get('to')).toBe('2026-10-05T12:00:00.000Z');
    expect(req.request.params.get('siteId')).toBe('Madrid/1');
    expect(req.request.params.get('sensorId')).toBe('s2');
    expect(req.request.params.get('sourceIp')).toBe('2001:db8::1');
    expect(req.request.params.get('destinationIp')).toBe('192.0.2.2');
    expect(req.request.params.get('protocol')).toBe('TCP');
    expect(req.request.params.get('sourcePort')).toBe('0');
    expect(req.request.params.get('destinationPort')).toBe('65535');
    expect(req.request.params.get('pageSize')).toBe('100');
    expect(req.request.params.get('cursor')).toBe('opaque/+=');
    expect(req.request.headers.keys()).toEqual([]);
    const body = { items: [], freshness: { state: 'recovering', measuredAt: filters.to } };
    req.flush(body);
    expect(await result).toEqual(body);
    http.verify();
  });
});
