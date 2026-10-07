import { expect, request as playwrightRequest } from '@playwright/test';
import { readFileSync } from 'node:fs';
import http from 'node:http';
import { searchByIp } from './console.mjs';

// Shared pieces of the browser tests that run against the real Keycloak of the laboratory (ADR-022 to ADR-026).
// The browser reaches Keycloak on the loopback address over plain HTTP (no certificate to trust); only the API's own channel to Keycloak is TLS (ADR-025).
export const AUTHORITY = 'http://127.0.0.1:8081/realms/monitoring';
export const API = 'http://127.0.0.1:5080';
export const PASSWORD = process.env.KEYCLOAK_LAB_PASSWORD ?? readFileSync(new URL('../../lab-secrets/keycloak-lab.txt', import.meta.url), 'utf8').trim();
export const authConfig = { enabled: true, authority: AUTHORITY, clientId: 'monitoring-web', scope: 'openid' };

// Signs in through the real Keycloak form.
export async function signIn(page, username) {
  await page.goto('/sessions');
  await page.waitForURL(`${AUTHORITY}/**`);
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(PASSWORD);
  await page.locator('#kc-login').click();
  await page.waitForURL('**/sessions');
  await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible();
}

export function decode(token, part = 1) {
  return JSON.parse(Buffer.from(token.split('.')[part], 'base64url').toString('utf8'));
}

export { searchByIp };

// The token the SPA holds in memory only: the test observes it on the wire, like any proxy would.
export function captureToken(page) {
  const holder = { token: null };
  page.on('request', request => {
    const value = request.headers()['authorization'];
    if (value?.startsWith('Bearer ')) holder.token = value.slice(7);
  });
  return holder;
}

// A token for `username` from a fresh browser context, so it comes from a new provider session and is signed with the key active now.
export async function mintToken(browser, username) {
  const context = await browser.newContext({ baseURL: 'http://127.0.0.1:4200' });
  try {
    const page = await context.newPage();
    await page.route('**/auth-config.json', route => route.fulfill({ json: authConfig }));
    const holder = captureToken(page);
    await signIn(page, username);
    await searchByIp(page, '198.18.0.1');
    await expect.poll(() => holder.token, { timeout: 30_000 }).not.toBeNull();
    return holder.token;
  } finally {
    await context.close();
  }
}

// Status the API gives a bearer token on an operation the analyst role may use.
export async function apiStatus(token, path = '/api/v1/inventory/devices') {
  const api = await playwrightRequest.newContext({ baseURL: API, extraHTTPHeaders: { authorization: `Bearer ${token}` } });
  try { return (await api.get(path)).status(); } finally { await api.dispose(); }
}

function send(method, path, { form, json, token } = {}) {
  const body = form ? new URLSearchParams(form).toString() : json === undefined ? undefined : JSON.stringify(json);
  const headers = {};
  if (form) headers['content-type'] = 'application/x-www-form-urlencoded';
  if (json !== undefined) headers['content-type'] = 'application/json';
  if (token) headers.authorization = `Bearer ${token}`;
  return new Promise((resolve, reject) => {
    const request = http.request({ host: '127.0.0.1', port: 8081, method, path, headers }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        resolve({ status: response.statusCode, headers: response.headers, body: text ? safeJson(text) : undefined });
      });
    });
    request.on('error', reject);
    request.end(body);
  });
}
const safeJson = text => { try { return JSON.parse(text); } catch { return text; } };

// Whether the provider answers its discovery document.
export async function discoveryStatus() {
  return (await send('GET', '/realms/monitoring/.well-known/openid-configuration')).status;
}

// Keycloak's administration REST API as the laboratory administrator.
export async function admin() {
  // The administrator's token lives a minute, so each call signs in again: a long test must still be able to clean up after itself.
  const signIn = async () => {
    const login = await send('POST', '/realms/master/protocol/openid-connect/token', { form: { grant_type: 'password', client_id: 'admin-cli', username: 'lab-admin', password: PASSWORD } });
    if (login.status !== 200) throw new Error(`Administrator sign-in failed (${login.status}).`);
    return login.body.access_token;
  };
  const call = async (method, path, json, expected) => {
    const response = await send(method, `/admin/realms/monitoring${path}`, { json, token: await signIn() });
    if (!expected.includes(response.status)) throw new Error(`${method} ${path} -> ${response.status} ${JSON.stringify(response.body)}`);
    return response;
  };
  return {
    // Adds an active RSA signing key that outranks the current one, so new tokens are signed with it.
    async addSigningKey(name = `lab-rotated-${Date.now()}`) {
      const realm = (await call('GET', '', undefined, [200])).body;
      const created = await call('POST', '/components', {
        name, providerId: 'rsa-generated', providerType: 'org.keycloak.keys.KeyProvider', parentId: realm.id,
        config: { priority: ['200'], enabled: ['true'], active: ['true'], keySize: ['2048'] }
      }, [201]);
      return created.headers.location.split('/').pop();
    },
    async removeSigningKey(id) { await call('DELETE', `/components/${id}`, undefined, [204, 404]); },
    async setAccessTokenLifespan(seconds) { await call('PUT', '', { accessTokenLifespan: seconds }, [204]); },
    async accessTokenLifespan() { return (await call('GET', '', undefined, [200])).body.accessTokenLifespan; }
  };
}
