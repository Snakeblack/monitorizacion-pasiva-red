import { test, expect, request as playwrightRequest } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { http, until } from '../../scripts/lab/compose.mjs';
import { ingestSyntheticSession } from '../stack/fixtures.mjs';
import { AUTHORITY, API, authConfig, signIn, decode, searchByIp, captureToken } from './identity-lab.mjs';

// Authenticated browser test against a real Keycloak (ADR-022, ADR-023; task 3.2). Requires the core stack, the API in OIDC mode and the
// `monitoring` realm: docker compose ... -f deploy/compose.e2e.yaml -f deploy/compose.identity.yaml up -d, then E2E_IDENTITY=1.
// The realm's four synthetic users cross two sites: administrador-inventario sees both, analista only A, auditor only B, and
// sin-ambito carries a valid role but no monitoring_scopes.
test.skip(process.env.E2E_IDENTITY !== '1', 'Needs the Keycloak overlay (deploy/compose.identity.yaml); set E2E_IDENTITY=1.');

const SITE_A = { site: 'pipeline-site', sensor: 'pipeline-sensor' };
const SITE_B = { site: 'lab-site-b', sensor: 'lab-sensor-b' };
const seeded = {};

test.beforeAll(async () => {
  // One address per site and per run, from the benchmarking range, so nothing seeded by other specs or earlier runs can match: a search
  // by IP then proves exactly which of these two sessions a user can see.
  for (const [name, scope] of [['a', SITE_A], ['b', SITE_B]]) {
    const eventId = `e2e-id-${name}-${randomUUID()}`;
    const sourceIp = `198.18.${Math.floor(Math.random() * 256)}.${1 + Math.floor(Math.random() * 254)}`;
    const startedAt = new Date(Date.now() - 5 * 60 * 1000);
    const searchDocumentId = await ingestSyntheticSession(eventId, {
      sourceIp, startedAt: startedAt.toISOString(), endedAt: new Date(startedAt.getTime() + 500).toISOString(), site: scope.site, sensor: scope.sensor
    });
    seeded[name] = { eventId, sourceIp, scope, searchDocumentId };
  }
  for (const { searchDocumentId } of Object.values(seeded))
    await until(() => http('elasticsearch:9200', `/sessions-v2-000001/_doc/${searchDocumentId}`), x => x.status === 200, 60);
  http('elasticsearch:9200', '/sessions-v2-000001/_refresh', 'POST');
});

test.beforeEach(async ({ page }) => {
  // The SPA ships `{ "enabled": false }`; a deployment provides its own file, which the browser test stands in for.
  await page.route('**/auth-config.json', route => route.fulfill({ json: authConfig }));
});

const asScope = ({ site, sensor }) => JSON.stringify({ site, sensor });

test('an anonymous visitor is sent to the identity provider and the discovery, PKCE and CORS exchange work from the SPA origin', async ({ page }) => {
  const discovery = page.waitForResponse(`${AUTHORITY}/.well-known/openid-configuration`);
  await page.goto('/sessions');
  expect((await discovery).status()).toBe(200);
  await page.waitForURL(`${AUTHORITY}/**`);
  const url = new URL(page.url());
  expect(url.searchParams.get('code_challenge_method')).toBe('S256');
  expect(url.searchParams.get('redirect_uri')).toBe('http://127.0.0.1:4200/auth/callback');
});

test('the analyst signs in, the token carries the realm contract and only site A is visible', async ({ page }) => {
  const holder = captureToken(page);
  await signIn(page, 'analista');
  await searchByIp(page, seeded.a.sourceIp);
  await expect(page.getByRole('link', { name: `Abrir detalle ${seeded.a.eventId}` })).toBeVisible({ timeout: 30_000 });

  const claims = decode(holder.token);
  expect(claims.iss).toBe(AUTHORITY);
  expect([claims.aud].flat()).toContain('monitoring-api');
  expect(claims.roles).toEqual(['analista']);
  expect(claims.monitoring_scopes).toEqual([asScope(SITE_A)]);
  expect(claims.sub).toMatch(/^[0-9a-f-]{36}$/);

  // Site B exists, but nothing about it is returned to someone who is not scoped to it.
  await searchByIp(page, seeded.b.sourceIp);
  await expect(page.locator('.results [role=status]').first()).toContainText('Sin coincidencias');
  await expect(page.getByRole('link', { name: `Abrir detalle ${seeded.b.eventId}` })).toHaveCount(0);

  const api = await playwrightRequest.newContext({ baseURL: API, extraHTTPHeaders: { authorization: `Bearer ${holder.token}` } });
  const foreign = await api.get(`/api/v1/sessions/${seeded.b.eventId}`, { params: { siteId: SITE_B.site, sensorId: SITE_B.sensor } });
  expect(foreign.status()).toBe(403);
  expect(await foreign.text()).not.toContain(seeded.b.sourceIp);
  const own = await api.get(`/api/v1/sessions/${seeded.a.eventId}`, { params: { siteId: SITE_A.site, sensorId: SITE_A.sensor } });
  expect(own.status()).toBe(200);
  const candidates = await api.get('/api/v1/inventory/candidates');
  expect(candidates.status()).toBe(200);
  await api.dispose();
});

test('an authenticated list opens the five-field detail of an in-scope session', async ({ page }) => {
  await signIn(page, 'analista');
  await searchByIp(page, seeded.a.sourceIp);
  const open = page.getByRole('link', { name: `Abrir detalle ${seeded.a.eventId}` });
  await expect(open).toBeVisible({ timeout: 30_000 });
  await open.click();
  await expect(page.getByRole('heading', { name: 'Detalle de sesión' })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('Sesión en ámbito');
  for (const label of ['eventId', 'siteId', 'sensorId', 'occurredAt', 'data'])
    await expect(page.locator('dt', { hasText: new RegExp(`^${label}$`) })).toBeVisible();
  await expect(page.locator('dd').first()).toHaveText(seeded.a.eventId);
});

test('the inventory administrator sees both sites', async ({ page }) => {
  const holder = captureToken(page);
  await signIn(page, 'admin-inventario');
  for (const name of ['a', 'b']) {
    await searchByIp(page, seeded[name].sourceIp);
    await expect(page.getByRole('link', { name: `Abrir detalle ${seeded[name].eventId}` })).toBeVisible({ timeout: 30_000 });
  }
  const claims = decode(holder.token);
  expect(claims.roles).toEqual(['administrador-inventario']);
  expect([...claims.monitoring_scopes].sort()).toEqual([asScope(SITE_A), asScope(SITE_B)].sort());
});

test('the auditor sees only site B and the role matrix denies the candidate queue', async ({ page }) => {
  const holder = captureToken(page);
  await signIn(page, 'auditor');
  await searchByIp(page, seeded.b.sourceIp);
  await expect(page.getByRole('link', { name: `Abrir detalle ${seeded.b.eventId}` })).toBeVisible({ timeout: 30_000 });
  await searchByIp(page, seeded.a.sourceIp);
  await expect(page.locator('.results [role=status]').first()).toContainText('Sin coincidencias');

  const api = await playwrightRequest.newContext({ baseURL: API, extraHTTPHeaders: { authorization: `Bearer ${holder.token}` } });
  expect((await api.get('/api/v1/inventory/devices')).status()).toBe(200);
  expect((await api.get('/api/v1/inventory/candidates')).status()).toBe(403);
  await api.dispose();
});

test('a valid role without monitoring_scopes is denied with no data', async ({ page }) => {
  const holder = captureToken(page);
  await signIn(page, 'sin-ambito');
  await searchByIp(page, seeded.a.sourceIp);
  await expect(page.getByRole('alert').filter({ hasText: 'Acceso denegado' })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('link', { name: /Abrir detalle/ })).toHaveCount(0);
  const claims = decode(holder.token);
  expect(claims.roles).toEqual(['analista']);
  expect(claims.monitoring_scopes).toBeUndefined();
});

test('signing out ends the provider session through the end_session endpoint', async ({ page }) => {
  await signIn(page, 'analista');
  await page.getByRole('button', { name: 'Cerrar sesión' }).click();
  await page.waitForURL('**/signed-out');
  // The provider redirects to the registered post_logout_redirect_uri as a fresh page load, so the in-memory reason is gone and the
  // generic message is shown; nothing from the previous session remains.
  await expect(page.getByRole('status')).toContainText('No has iniciado sesión');
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toHaveCount(0);
  // The provider session is gone too: going back to a work surface asks for credentials again instead of signing in silently.
  await page.goto('/sessions');
  await page.waitForURL(`${AUTHORITY}/**`);
  await expect(page.locator('#username')).toBeVisible();
});
