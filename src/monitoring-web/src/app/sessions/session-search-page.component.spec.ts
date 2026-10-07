import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService } from '../auth/auth.service';
import { SessionDetailPage } from '../session-detail/session-detail-page.component';
import { SessionPage, SessionSummary } from './session-search-api';
import { SessionSearchPage } from './session-search-page.component';

const row: SessionSummary = {
  eventId: 'evt/a',
  siteId: 'Madrid',
  sensorId: 'sonda-1',
  sourceIp: '192.0.2.1',
  destinationIp: '2001:db8::2',
  sourcePort: 0,
  destinationPort: 443,
  protocol: 'TCP',
  startedAt: '2026-10-05T10:00:00Z',
  endedAt: '2026-10-05T10:00:01Z',
  provenance: 'synthetic',
};
const page: SessionPage = {
  items: [row],
  nextCursor: 'page-2',
  freshness: { state: 'current', measuredAt: '2026-10-05T12:00:00.123Z', lagSeconds: 3 },
};

async function openSearch(extraProviders: unknown[] = []) {
  vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-10-05T12:00:00.123Z'));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      ...(extraProviders as never[]),
      provideRouter([
        { path: 'sessions', component: SessionSearchPage },
        { path: 'sessions/:eventId', component: SessionDetailPage },
      ]),
    ],
  });
  const harness = await RouterTestingHarness.create('/sessions');
  const http = TestBed.inject(HttpTestingController);
  const root = () => harness.routeNativeElement as HTMLElement;
  const text = () => root().textContent ?? '';
  const request = () => http.expectOne((req) => req.url === '/api/v1/sessions');
  const button = (label: string) => {
    const element = [...root().querySelectorAll('button')].find(
      (b) => b.textContent?.trim() === label,
    );
    expect(element, label).toBeInstanceOf(HTMLButtonElement);
    return element as HTMLButtonElement;
  };
  // Filters are added from the "+ Filtro" menu and the custom range from its own button, as a person would.
  const reveal = (name: string) => {
    if (root().querySelector(`#${name}`)) return;
    if (name === 'from' || name === 'to') {
      button('Personalizado').click();
    } else {
      const add = [...root().querySelectorAll('button')].find((b) => b.textContent?.includes('Filtro')) as HTMLButtonElement;
      add.click();
      harness.fixture.detectChanges();
      const labels: Record<string, string> = { sourceIp: 'IP origen', destinationIp: 'IP destino', siteId: 'Sede', sensorId: 'Sonda' };
      const item = [...root().querySelectorAll('[role="menuitem"]')].find((candidate) => candidate.textContent?.trim().startsWith(labels[name])) as HTMLButtonElement;
      item.click();
    }
    harness.fixture.detectChanges();
  };
  const field = (name: string, value: string) => {
    reveal(name);
    const element = root().querySelector(`#${name}`) as HTMLInputElement;
    expect(element, name).toBeInstanceOf(HTMLInputElement);
    element.value = value;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    harness.fixture.detectChanges();
  };
  const submit = async () => {
    root()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    harness.fixture.detectChanges();
  };
  return { harness, http, root, text, request, button, field, submit };
}

describe('SessionSearchPage', () => {
  afterEach(() => vi.restoreAllMocks());

  it('queries recent 24h at page 50 without optional filters and announces loading/empty/current', async () => {
    const ctx = await openSearch();
    expect(ctx.text()).toContain('Consultando sesiones');
    const request = ctx.request();
    expect(request.request.params.get('from')).toBe('2026-10-04T12:00:00.123Z');
    expect(request.request.params.get('to')).toBe('2026-10-05T12:00:00.123Z');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.keys().sort()).toEqual(['from', 'pageSize', 'to']);
    request.flush({ ...page, items: [], nextCursor: null });
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('Sin coincidencias');
    expect(ctx.text()).toContain('Índice al día');
    expect(ctx.root().querySelector('table')).toBeNull();
    expect(ctx.root().querySelector('[role="status"]')?.textContent).toContain('Sin coincidencias');
    ctx.http.verify();
  });

  it('shows scoped rows, lag and complete identity on a keyboard reachable detail link', async () => {
    const ctx = await openSearch();
    ctx
      .request()
      .flush({
        ...page,
        freshness: { state: 'lagging', measuredAt: page.freshness.measuredAt, lagSeconds: 72 },
      });
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('Madrid');
    expect(ctx.text()).toContain('sonda-1');
    expect(ctx.text()).toContain('192.0.2.1');
    expect(ctx.text()).toContain('2001:db8::2');
    expect(ctx.text()).toContain('72 s');
    expect(ctx.text()).toContain('Índice retrasado');
    const link = ctx
      .root()
      .querySelector('a[aria-label="Abrir detalle evt/a"]') as HTMLAnchorElement;
    expect(link).toBeInstanceOf(HTMLAnchorElement);
    expect(link.getAttribute('href')).toBe('/sessions/evt%2Fa?siteId=Madrid&sensorId=sonda-1');
    await ctx.harness.navigateByUrl(link.getAttribute('href')!, SessionDetailPage);
    const detail = ctx.http.expectOne((req) => req.url === '/api/v1/sessions/evt%2Fa');
    expect(detail.request.params.get('siteId')).toBe('Madrid');
    expect(detail.request.params.get('sensorId')).toBe('sonda-1');
    detail.flush({
      eventId: row.eventId,
      siteId: row.siteId,
      sensorId: row.sensorId,
      occurredAt: row.startedAt,
      data: { protocol: 'TCP' },
    });
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('Sesión en ámbito');
    expect(ctx.root().querySelectorAll('dt')).toHaveLength(5);
    ctx.http.verify();
  });

  it('keeps query bounds on next page and removes cursor/results when an exact filter changes', async () => {
    const ctx = await openSearch();
    ctx.request().flush(page);
    await ctx.harness.fixture.whenStable();
    ctx.button('Página siguiente').click();
    ctx.harness.fixture.detectChanges();
    const next = ctx.request();
    expect(next.request.params.get('cursor')).toBe('page-2');
    expect(next.request.params.get('from')).toBe('2026-10-04T12:00:00.123Z');
    next.flush({ ...page, nextCursor: null });
    await ctx.harness.fixture.whenStable();
    ctx.field('sourceIp', '192.0.2.99');
    expect(ctx.text()).not.toContain('evt/a');
    await ctx.submit();
    const fresh = ctx.request();
    expect(fresh.request.params.has('cursor')).toBe(false);
    expect(fresh.request.params.get('sourceIp')).toBe('192.0.2.99');
    fresh.flush({ ...page, items: [], nextCursor: null });
    await ctx.harness.fixture.whenStable();
    ctx.http.verify();
  });

  it('cancels a pending old search when filters change and cannot display its stale response', async () => {
    const ctx = await openSearch();
    const old = ctx.request();
    ctx.field('sourceIp', '192.0.2.99');
    await vi.waitFor(() => expect(old.cancelled).toBe(true));
    await ctx.submit();
    ctx
      .request()
      .flush({ ...page, items: [{ ...row, eventId: 'new-selection' }], nextCursor: null });
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('new-selection');
    expect(ctx.text()).not.toContain('evt/a');
    ctx.http.verify();
  });

  it('shows visible validation for >24h without site/sensor/IP and page 101 without HTTP', async () => {
    const ctx = await openSearch();
    ctx.request().flush(page);
    await ctx.harness.fixture.whenStable();
    ctx.field('from', '2026-10-03T12:00:00Z');
    await ctx.submit();
    expect(ctx.root().querySelector('[role="alert"]')?.textContent).toContain(
      'sede, sonda y una IP',
    );
    // The missing pieces are offered as one-press additions next to the message.
    expect(ctx.text()).toContain('Más de 24 h solo se consulta con sede, sonda y una IP');
    ctx.http.expectNone((req) => req.url === '/api/v1/sessions');
    // A malformed address is flagged on its own filter and never reaches the server.
    ctx.field('from', '2026-10-04T12:00:00.123Z');
    ctx.field('sourceIp', '999.0.0.1');
    await ctx.submit();
    expect(ctx.text()).toContain('IP origen: Escribe una dirección IPv4 o IPv6 completa');
    ctx.http.expectNone((req) => req.url === '/api/v1/sessions');
    ctx.http.verify();
  });

  it.each([
    { status: 400, message: 'Consulta inválida' },
    { status: 401, message: 'sin proveedor de identidad' }, // the test console has no identity provider configured
    { status: 403, message: 'Acceso denegado' },
    { status: 429, message: 'Demasiadas consultas' },
    { status: 503, message: 'Búsqueda no disponible' },
    { status: 504, message: 'tiempo de espera' },
  ])(
    'distinguishes HTTP $status from no matches and clears previous data',
    async ({ status, message }) => {
      const ctx = await openSearch();
      ctx.request().flush(page);
      await ctx.harness.fixture.whenStable();
      ctx.button('Página siguiente').click();
      ctx.harness.fixture.detectChanges();
      ctx.request().flush('', { status, statusText: 'Failure' });
      await ctx.harness.fixture.whenStable();
      expect(ctx.root().querySelector('[role="alert"]')?.textContent).toContain(message);
      expect(ctx.text()).not.toContain('Sin coincidencias');
      expect(ctx.text()).not.toContain('evt/a');
      expect(ctx.button('Página siguiente').disabled).toBe(true);
      ctx.http.verify();
    },
  );

  it('announces expired snapshot and restarts from page one, using a native action', async () => {
    const ctx = await openSearch();
    ctx.request().flush(page);
    await ctx.harness.fixture.whenStable();
    ctx.button('Página siguiente').click();
    ctx.harness.fixture.detectChanges();
    ctx.request().flush('', { status: 410, statusText: 'Gone' });
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('ha caducado');
    const restart = ctx.button('Reiniciar consulta');
    expect(restart.type).toBe('button');
    restart.click();
    ctx.harness.fixture.detectChanges();
    const fresh = ctx.request();
    expect(fresh.request.params.has('cursor')).toBe(false);
    fresh.flush({ ...page, nextCursor: null });
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('evt/a');
    ctx.http.verify();
  });

  it('reports network failure, unknown lag and captured inference/partiality without fake values', async () => {
    const ctx = await openSearch();
    ctx.request().error(new ProgressEvent('error'));
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('No se pudo conectar');
    ctx.button('Reintentar consulta').click();
    ctx.harness.fixture.detectChanges();
    ctx
      .request()
      .flush({
        items: [{ ...row, provenance: 'capture', inferred: true, partial: true }],
        freshness: { state: 'recovering', measuredAt: page.freshness.measuredAt },
      });
    await ctx.harness.fixture.whenStable();
    expect(ctx.text()).toContain('Índice en recuperación');
    expect(ctx.text()).toContain('Retraso desconocido');
    expect(ctx.text()).toContain('Capturada · inferida · parcial');
    expect(ctx.text()).not.toContain('Retraso 0 s');
    ctx.http.verify();
  });

  it('asks the person to sign in on a 401 when an identity provider is configured', async () => {
    const ctx = await openSearch([{ provide: AuthService, useValue: { enabled: true } }]);
    ctx.request().flush('', { status: 401, statusText: 'Unauthorized' });
    await ctx.harness.fixture.whenStable();
    expect(ctx.root().querySelector('[role="alert"]')?.textContent).toContain('Inicia sesión');
    ctx.http.verify();
  });

  it('adds a filter from the menu, lets it be removed, and a preset search always ends now', async () => {
    const ctx = await openSearch();
    ctx.request().flush(page);
    await ctx.harness.fixture.whenStable();
    ctx.field('sourceIp', '192.0.2.99');
    expect(ctx.root().querySelector('#sourceIp')).toBeInstanceOf(HTMLInputElement);
    (ctx.root().querySelector('button[aria-label="Quitar filtro IP origen"]') as HTMLButtonElement).click();
    ctx.harness.fixture.detectChanges();
    expect(ctx.root().querySelector('#sourceIp')).toBeNull();
    // Time passes between choosing "1 h" and the search: the range is measured when it runs.
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-10-05T12:30:00.000Z'));
    (ctx.root().querySelector('button[aria-label="Última hora"]') as HTMLButtonElement).click();
    ctx.harness.fixture.detectChanges();
    const hour = ctx.request();
    expect(hour.request.params.get('from')).toBe('2026-10-05T11:30:00.000Z');
    expect(hour.request.params.get('to')).toBe('2026-10-05T12:30:00.000Z');
    expect(hour.request.params.has('sourceIp')).toBe(false);
    hour.flush({ ...page, nextCursor: null });
    await ctx.harness.fixture.whenStable();
    ctx.http.verify();
  });
});
