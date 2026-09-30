import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import type { SessionDetail } from './session-detail-api';
import { SessionDetailPage } from './session-detail-page.component';

const foundSession: SessionDetail = {
  data: { protocol: 'TCP', sourceIp: '192.0.2.1' },
  eventId: 'shared',
  occurredAt: '2026-09-29T12:00:00.100Z',
  sensorId: 'sensor-a',
  siteId: 'site-a',
};

async function openSession(eventId: string) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'sessions/:eventId', component: SessionDetailPage }]),
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });

  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(`/sessions/${eventId}`, SessionDetailPage);
  const http = TestBed.inject(HttpTestingController);
  return {
    harness,
    http,
    text: () => harness.routeNativeElement?.textContent ?? '',
  };
}

function retryButton(root: HTMLElement | null): HTMLButtonElement {
  const button = Array.from(root?.querySelectorAll('button') ?? []).find(
    (candidate) => candidate.textContent?.trim() === 'Reintentar consulta',
  );
  expect(button).toBeInstanceOf(HTMLButtonElement);
  expect(button?.type).toBe('button');
  return button as HTMLButtonElement;
}

describe('SessionDetailPage', () => {
  it('shows five persisted fields and success text on HTTP 200', async () => {
    const { harness, http, text } = await openSession('shared');

    expect(text()).toContain('Consultando detalle');

    const req = http.expectOne('/api/v1/sessions/shared');
    expect(req.request.method).toBe('GET');
    expect(req.request.headers.has('X-Site-Id')).toBe(false);
    expect(req.request.headers.has('X-Sensor-Id')).toBe(false);
    req.flush(foundSession);

    await harness.fixture.whenStable();

    const shown = text();
    expect(shown).toContain('Sesión en ámbito');
    expect(shown).toContain('shared');
    expect(shown).toContain('site-a');
    expect(shown).toContain('sensor-a');
    expect(shown).toContain('2026-09-29T12:00:00.100Z');
    expect(shown).toContain('TCP');
    expect(shown).toContain('192.0.2.1');
    expect(shown).not.toContain('Consultando detalle');
    http.verify();
  });

  it('shows a different 200 payload as the five field values', async () => {
    const other: SessionDetail = {
      data: { protocol: 'UDP' },
      eventId: 'other-event',
      occurredAt: '2026-09-29T12:00:00Z',
      sensorId: 'sensor-b',
      siteId: 'site-b',
    };
    const { harness, http, text } = await openSession('other-event');

    http.expectOne('/api/v1/sessions/other-event').flush(other);
    await harness.fixture.whenStable();

    const shown = text();
    expect(shown).toContain('Sesión en ámbito');
    expect(shown).toContain('other-event');
    expect(shown).toContain('site-b');
    expect(shown).toContain('sensor-b');
    expect(shown).toContain('2026-09-29T12:00:00Z');
    expect(shown).toContain('UDP');
    expect(shown).not.toContain('site-a');
    http.verify();
  });

  it('shows an empty state on HTTP 404 without session field rows', async () => {
    const { harness, http, text } = await openSession('missing');

    http.expectOne('/api/v1/sessions/missing').flush('not found', {
      status: 404,
      statusText: 'Not Found',
    });
    await harness.fixture.whenStable();

    const shown = text();
    expect(shown).toContain('No hay sesión en este ámbito');
    expect(shown).not.toContain('Sesión en ámbito');
    expect(shown).not.toContain('No se pudo consultar el detalle');
    expect(harness.routeNativeElement?.querySelectorAll('dt')).toHaveLength(0);
    expect(shown).not.toContain('not found');
    http.verify();
  });

  it('shows the same empty state when another scope event returns 404', async () => {
    const { harness, http, text } = await openSession('other-only');

    http.expectOne('/api/v1/sessions/other-only').flush('', {
      status: 404,
      statusText: 'Not Found',
    });
    await harness.fixture.whenStable();

    const shown = text();
    expect(shown).toContain('No hay sesión en este ámbito');
    expect(harness.routeNativeElement?.querySelectorAll('dt')).toHaveLength(0);
    expect(shown).not.toContain('site-b');
    http.verify();
  });

  it('shows an error state on HTTP 401 without session fields', async () => {
    const { harness, http, text } = await openSession('shared');

    http.expectOne('/api/v1/sessions/shared').flush('', {
      status: 401,
      statusText: 'Unauthorized',
    });
    await harness.fixture.whenStable();

    const shown = text();
    expect(shown).toContain('No se pudo consultar el detalle');
    expect(shown).not.toContain('No hay sesión en este ámbito');
    expect(shown).not.toContain('Sesión en ámbito');
    expect(harness.routeNativeElement?.querySelectorAll('dt')).toHaveLength(0);
    http.verify();
  });

  it('shows an error state on network failure (status 0) without session fields', async () => {
    const { harness, http, text } = await openSession('shared');

    http.expectOne('/api/v1/sessions/shared').error(new ProgressEvent('error'));
    await harness.fixture.whenStable();

    const shown = text();
    expect(shown).toContain('No se pudo consultar el detalle');
    expect(shown).not.toContain('No hay sesión en este ámbito');
    expect(harness.routeNativeElement?.querySelectorAll('dt')).toHaveLength(0);
    http.verify();
  });

  it('retries the GET when Reintentar consulta is clicked', async () => {
    const { harness, http, text } = await openSession('shared');

    http.expectOne('/api/v1/sessions/shared').flush('', {
      status: 401,
      statusText: 'Unauthorized',
    });
    await harness.fixture.whenStable();

    const retry = retryButton(harness.routeNativeElement);
    retry.click();
    harness.fixture.detectChanges();

    const second = http.expectOne('/api/v1/sessions/shared');
    expect(second.request.method).toBe('GET');
    second.flush(foundSession);
    await harness.fixture.whenStable();

    expect(text()).toContain('Sesión en ámbito');
    expect(text()).toContain('shared');
    http.verify();
  });

  it.each([
    { key: 'Enter', code: 'Enter' },
    { key: ' ', code: 'Space' },
  ])('retries the GET on keydown $code without relying on a synthetic click', async ({ key, code }) => {
    const { harness, http, text } = await openSession('shared');

    http.expectOne('/api/v1/sessions/shared').flush('', {
      status: 401,
      statusText: 'Unauthorized',
    });
    await harness.fixture.whenStable();

    const retry = retryButton(harness.routeNativeElement);
    retry.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, code, key }));
    harness.fixture.detectChanges();

    const second = http.expectOne('/api/v1/sessions/shared');
    expect(second.request.method).toBe('GET');
    second.flush(foundSession);
    await harness.fixture.whenStable();

    expect(text()).toContain('Sesión en ámbito');
    http.verify();
  });

  it('chains HTTP 200, 404, and 401 in the same page suite', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'sessions/:eventId', component: SessionDetailPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    const harness = await RouterTestingHarness.create();
    const http = TestBed.inject(HttpTestingController);
    const text = () => harness.routeNativeElement?.textContent ?? '';

    await harness.navigateByUrl('/sessions/shared', SessionDetailPage);
    http.expectOne('/api/v1/sessions/shared').flush(foundSession);
    await harness.fixture.whenStable();
    expect(text()).toContain('Sesión en ámbito');
    expect(text()).toContain('site-a');
    expect(text()).toContain('TCP');

    await harness.navigateByUrl('/sessions/missing', SessionDetailPage);
    http.expectOne('/api/v1/sessions/missing').flush('', {
      status: 404,
      statusText: 'Not Found',
    });
    await harness.fixture.whenStable();
    expect(text()).toContain('No hay sesión en este ámbito');
    expect(harness.routeNativeElement?.querySelectorAll('dt')).toHaveLength(0);
    expect(text()).not.toContain('site-a');

    await harness.navigateByUrl('/sessions/denied', SessionDetailPage);
    http.expectOne('/api/v1/sessions/denied').flush('', {
      status: 401,
      statusText: 'Unauthorized',
    });
    await harness.fixture.whenStable();
    expect(text()).toContain('No se pudo consultar el detalle');
    expect(text()).not.toContain('No hay sesión en este ámbito');
    expect(harness.routeNativeElement?.querySelectorAll('dt')).toHaveLength(0);
    http.verify();
  });
});
