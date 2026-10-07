import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { CandidateView, DeviceView } from './inventory-api';
import { InventoryPage } from './inventory-page.component';

const device: DeviceView = { deviceId: '11111111-1111-1111-1111-111111111111', name: 'Impresora planta 2', description: 'HP', revision: 3, scopes: [{ siteId: 'madrid', sensorId: 's1' }], updatedAt: '2026-10-06T10:00:00Z' };
const candidate: CandidateView = {
  candidateId: '22222222-2222-2222-2222-222222222222', siteId: 'madrid', sensorId: 's1', mac: 'aa:bb:cc:00:00:01', vlanId: null,
  firstSeen: '2026-10-06T09:00:00Z', lastSeen: '2026-10-06T10:00:00Z', state: 'candidate', revision: 1, deviceId: null,
  associations: [{ ip: '192.0.2.10', firstSeen: '2026-10-06T09:00:00Z', lastSeen: '2026-10-06T10:00:00Z' }],
};

function setup(roles: string[] = ['administrador-inventario'], enabled = true) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { enabled, roles: signal(roles) } as unknown as AuthService },
    ],
  });
  const fixture = TestBed.createComponent(InventoryPage);
  const http = TestBed.inject(HttpTestingController);
  return { fixture, http };
}

async function settle(fixture: ComponentFixture<InventoryPage>) {
  fixture.detectChanges();
  await fixture.whenStable();
  // Let the async continuations that follow a flushed response run before looking at the view.
  await new Promise((resolve) => setTimeout(resolve));
  fixture.detectChanges();
}

function text(fixture: ComponentFixture<InventoryPage>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

function button(fixture: ComponentFixture<InventoryPage>, label: string): HTMLButtonElement {
  const found = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((candidateButton) => candidateButton.textContent?.trim() === label);
  if (!found) throw new Error(`No button "${label}"`);
  return found;
}

function type(fixture: ComponentFixture<InventoryPage>, selector: string, value: string) {
  const input = (fixture.nativeElement as HTMLElement).querySelector(selector) as HTMLInputElement | HTMLSelectElement;
  input.value = value;
  input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
}

async function openCandidates(fixture: ComponentFixture<InventoryPage>, http: HttpTestingController, items: CandidateView[] = [candidate], nextCursor: string | null = null) {
  await settle(fixture);
  http.expectOne((request) => request.url === '/api/v1/inventory/devices').flush({ items: [device], nextCursor: null });
  await settle(fixture);
  button(fixture, 'Candidatos').click();
  await settle(fixture);
  http.expectOne((request) => request.url === '/api/v1/inventory/candidates').flush({ items, nextCursor });
  await settle(fixture);
}

describe('InventoryPage', () => {
  it('lists the devices the caller may read, with their revision', async () => {
    const { fixture, http } = setup(['analista']);
    await settle(fixture);
    const request = http.expectOne((r) => r.url === '/api/v1/inventory/devices');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [device], nextCursor: null });
    await settle(fixture);
    expect(text(fixture)).toContain('Impresora planta 2');
    expect(text(fixture)).toContain('madrid / s1');
    http.verify();
  });

  it('says so when a listing is empty', async () => {
    const { fixture, http } = setup();
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/devices').flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(fixture)).toContain('No hay dispositivos confirmados');
  });

  it('pages with the opaque cursor and appends', async () => {
    const { fixture, http } = setup();
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/devices').flush({ items: [device], nextCursor: 'opaque/+=' });
    await settle(fixture);
    button(fixture, 'Cargar más').click();
    await settle(fixture);
    const next = http.expectOne((r) => r.url === '/api/v1/inventory/devices');
    expect(next.request.params.get('cursor')).toBe('opaque/+=');
    next.flush({ items: [{ ...device, deviceId: '33333333-3333-3333-3333-333333333333', name: 'Router' }], nextCursor: null });
    await settle(fixture);
    expect(text(fixture)).toContain('Impresora planta 2');
    expect(text(fixture)).toContain('Router');
    expect(Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Cargar más')).toBe(false);
  });

  it.each([
    [401, 'Inicia sesión'],
    [403, 'Acceso denegado'],
    [503, 'no disponible'],
  ])('explains a %s refusal in text and shows no data', async (status, expected) => {
    const { fixture, http } = setup();
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/devices').flush({ detail: 'leaky' }, { status, statusText: 'x' });
    await settle(fixture);
    expect(text(fixture)).toContain(expected);
    expect(text(fixture)).not.toContain('leaky');
    expect((fixture.nativeElement as HTMLElement).querySelector('table')).toBeNull();
  });

  it('shows candidates with their IP associations but offers no decisions to a read-only role', async () => {
    const { fixture, http } = setup(['analista']);
    await openCandidates(fixture, http);
    expect(text(fixture)).toContain('aa:bb:cc:00:00:01');
    expect(text(fixture)).toContain('192.0.2.10');
    expect(text(fixture)).toContain('Sin VLAN');
    for (const label of ['Confirmar', 'Rechazar', 'Fusionar']) {
      expect(Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).some((b) => b.textContent?.trim() === label)).toBe(false);
    }
  });

  it('confirms a candidate sending the revision the user saw and refreshes the list', async () => {
    const { fixture, http } = setup();
    await openCandidates(fixture, http);
    button(fixture, 'Confirmar').click();
    await settle(fixture);
    type(fixture, 'input[name="deviceName"]', 'Portátil de Ana');
    type(fixture, 'input[name="deviceDescription"]', 'Planta 3');
    await settle(fixture);
    button(fixture, 'Confirmar candidato').click();
    await settle(fixture);
    const post = http.expectOne(`/api/v1/inventory/candidates/${candidate.candidateId}/confirm`);
    expect(post.request.body).toEqual({ expectedRevision: 1, deviceName: 'Portátil de Ana', deviceDescription: 'Planta 3' });
    post.flush({ ...candidate, state: 'confirmed', revision: 2 });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/candidates').flush({ items: [], nextCursor: null });
    await settle(fixture);
    expect(text(fixture)).toContain('Candidato confirmado');
  });

  it('reports a conflict, reloads the list and keeps nothing half done', async () => {
    const { fixture, http } = setup();
    await openCandidates(fixture, http);
    button(fixture, 'Rechazar').click();
    await settle(fixture);
    type(fixture, 'input[name="reason"]', 'equipo ajeno');
    await settle(fixture);
    button(fixture, 'Rechazar candidato').click();
    await settle(fixture);
    http.expectOne(`/api/v1/inventory/candidates/${candidate.candidateId}/reject`).flush({ code: 'stale-revision' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/candidates').flush({ items: [{ ...candidate, revision: 2 }], nextCursor: null });
    await settle(fixture);
    expect(text(fixture)).toContain('Otro usuario ha cambiado este registro');
  });

  it('requires a reason to reject', async () => {
    const { fixture, http } = setup();
    await openCandidates(fixture, http);
    button(fixture, 'Rechazar').click();
    await settle(fixture);
    expect(button(fixture, 'Rechazar candidato').disabled).toBe(true);
    type(fixture, 'input[name="reason"]', '   ');
    await settle(fixture);
    expect(button(fixture, 'Rechazar candidato').disabled).toBe(true);
    http.verify();
  });

  it('merges into a chosen device sending both revisions', async () => {
    const { fixture, http } = setup();
    await openCandidates(fixture, http);
    button(fixture, 'Fusionar').click();
    await settle(fixture);
    type(fixture, 'select[name="deviceId"]', device.deviceId);
    await settle(fixture);
    button(fixture, 'Fusionar candidato').click();
    await settle(fixture);
    const post = http.expectOne(`/api/v1/inventory/candidates/${candidate.candidateId}/merge`);
    expect(post.request.body).toEqual({ expectedRevision: 1, deviceId: device.deviceId, expectedDeviceRevision: 3 });
    post.flush({ ...candidate, state: 'confirmed', revision: 2, deviceId: device.deviceId });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/candidates').flush({ items: [], nextCursor: null });
  });

  it('renames a device with its revision and shows a denial in text when scopes do not allow it', async () => {
    const { fixture, http } = setup();
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/devices').flush({ items: [device], nextCursor: null });
    await settle(fixture);
    button(fixture, 'Editar').click();
    await settle(fixture);
    type(fixture, 'input[name="name"]', 'Impresora planta 3');
    await settle(fixture);
    button(fixture, 'Guardar dispositivo').click();
    await settle(fixture);
    const patch = http.expectOne(`/api/v1/inventory/devices/${device.deviceId}`);
    expect(patch.request.body).toEqual({ expectedRevision: 3, name: 'Impresora planta 3', description: 'HP' });
    patch.flush(null, { status: 403, statusText: 'Forbidden' });
    await settle(fixture);
    expect(text(fixture)).toContain('Acceso denegado');
    http.verify();
  });

  it('lists raw observations', async () => {
    const { fixture, http } = setup(['analista']);
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/devices').flush({ items: [], nextCursor: null });
    await settle(fixture);
    button(fixture, 'Observaciones').click();
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/inventory/observations').flush({
      items: [{ siteId: 'madrid', sensorId: 's1', eventId: 'e1', observedAt: '2026-10-06T10:00:00Z', ip: '192.0.2.10', mac: null, vlanId: 42 }],
      nextCursor: null,
    });
    await settle(fixture);
    expect(text(fixture)).toContain('192.0.2.10');
    expect(text(fixture)).toContain('42');
  });

  it('shows decisions without role information when authentication is not configured (development)', async () => {
    const { fixture, http } = setup([], false);
    await openCandidates(fixture, http);
    expect(button(fixture, 'Confirmar')).toBeInstanceOf(HTMLButtonElement);
  });
});
