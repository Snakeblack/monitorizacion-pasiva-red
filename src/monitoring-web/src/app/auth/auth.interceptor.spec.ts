import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SESSION_DETAIL_API_ORIGIN } from '../session-detail/session-detail-api';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

function setup(token: string | null, origin = '') {
  const expire = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { accessToken: () => token, expire } },
      { provide: SESSION_DETAIL_API_ORIGIN, useValue: origin },
    ],
  });
  return { http: TestBed.inject(HttpClient), controller: TestBed.inject(HttpTestingController), expire };
}

describe('authInterceptor', () => {
  it('sends the bearer token to the application API only', () => {
    const { http, controller } = setup('token-1');
    http.get('/api/v1/sessions').subscribe();
    expect(controller.expectOne('/api/v1/sessions').request.headers.get('Authorization')).toBe('Bearer token-1');
    http.get('https://other.example/api/v1/sessions').subscribe();
    expect(controller.expectOne('https://other.example/api/v1/sessions').request.headers.has('Authorization')).toBe(false);
    http.get('/assets/config.json').subscribe();
    expect(controller.expectOne('/assets/config.json').request.headers.has('Authorization')).toBe(false);
  });

  it('honours a configured API origin and still never leaks the token elsewhere', () => {
    const { http, controller } = setup('token-1', 'https://api.example');
    http.get('https://api.example/api/v1/sessions').subscribe();
    expect(controller.expectOne('https://api.example/api/v1/sessions').request.headers.get('Authorization')).toBe('Bearer token-1');
    http.get('/api/v1/sessions').subscribe();
    expect(controller.expectOne('/api/v1/sessions').request.headers.has('Authorization')).toBe(false);
  });

  it('sends nothing when there is no token (development or signed out)', () => {
    const { http, controller } = setup(null);
    http.get('/api/v1/sessions').subscribe();
    expect(controller.expectOne('/api/v1/sessions').request.headers.has('Authorization')).toBe(false);
  });

  it('ends the session when the API refuses a request that carried the token', () => {
    const { http, controller, expire } = setup('token-1');
    http.get('/api/v1/sessions').subscribe({ error: () => undefined });
    controller.expectOne('/api/v1/sessions').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(expire).toHaveBeenCalledWith('rejected');
  });

  it('does not end the session for a 403 or for a 401 on a request that had no token', () => {
    const { http, controller, expire } = setup('token-1');
    http.get('/api/v1/sessions').subscribe({ error: () => undefined });
    controller.expectOne('/api/v1/sessions').flush(null, { status: 403, statusText: 'Forbidden' });
    expect(expire).not.toHaveBeenCalled();
    TestBed.resetTestingModule();
    const anonymous = setup(null);
    anonymous.http.get('/api/v1/sessions').subscribe({ error: () => undefined });
    anonymous.controller.expectOne('/api/v1/sessions').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(anonymous.expire).not.toHaveBeenCalled();
  });
});
