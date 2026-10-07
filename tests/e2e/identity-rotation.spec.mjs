import { test, expect } from '@playwright/test';
import { spawnSync } from 'node:child_process';
import { admin, apiStatus, decode, discoveryStatus, mintToken, AUTHORITY } from './identity-lab.mjs';

// Key rotation and provider outage against the real Keycloak (ADR-025, ADR-026; task 3.2). Same setup as identity-keycloak.spec.mjs.
// Rotation: a new signing key is added to the realm while the API is running and must be trusted without a restart, with the old key
// still honoured. Outage: stopping the provider must not erase the keys the API already trusts, and recovery needs no intervention.
test.skip(process.env.E2E_IDENTITY !== '1', 'Needs the Keycloak overlay (deploy/compose.identity.yaml); set E2E_IDENTITY=1.');
test.describe.configure({ mode: 'serial' });

const container = `${process.env.MONITORING_COMPOSE_PROJECT ?? 'monitoring-local'}-keycloak-1`;
const docker = (...args) => {
  const result = spawnSync('docker', args, { encoding: 'utf8' });
  if (result.status !== 0) throw new Error(`docker ${args.join(' ')} failed: ${result.stderr}`);
};
const seconds = since => Math.round((Date.now() - since) / 1000);
const kidOf = token => decode(token, 0).kid;
const timeLeft = token => decode(token).exp - Date.now() / 1000;

let administrator;
let addedKey;
const issued = {};

test.beforeAll(async () => { administrator = await admin(); });
test.afterAll(async () => {
  // Leave the realm as it was found: the key added here is removed and the original key signs again.
  try { docker('start', container); } catch { /* already running */ }
  if (addedKey) await (await admin()).removeSigningKey(addedKey);
});

test('a signing key added to the realm is trusted without restarting the API and the previous key keeps working', async ({ browser }) => {
  test.setTimeout(240_000);
  const before = await mintToken(browser, 'analista');
  expect(await apiStatus(before)).toBe(200);

  addedKey = await administrator.addSigningKey();
  const after = await mintToken(browser, 'analista');
  expect(kidOf(after)).not.toBe(kidOf(before));
  issued.rotated = after;

  // The API does not know the new key id: it must fetch the key set again (never more often than once a minute) and then accept the token.
  const started = Date.now();
  await expect.poll(() => apiStatus(after), { timeout: 100_000, intervals: [2_000] }).toBe(200);
  console.log(`new key trusted ${seconds(started)} s after the first token signed with it`);
  expect(await apiStatus(before)).toBe(200);
  expect(timeLeft(before)).toBeGreaterThan(0);
});

test('while the provider is down a failed key refresh keeps the keys already trusted, and recovery needs no intervention', async ({ browser }) => {
  test.setTimeout(300_000);
  const token = issued.rotated;
  expect(timeLeft(token)).toBeGreaterThan(120);
  expect(await apiStatus(token)).toBe(200);

  docker('stop', container);
  // A token naming a key the API has never seen forces it to try to fetch the key set. Waiting past the one-minute floor makes sure
  // that attempt really happens (and fails) instead of being skipped.
  await new Promise(resolve => setTimeout(resolve, 65_000));
  const unknownKey = `${Buffer.from(JSON.stringify({ alg: 'RS256', kid: 'never-published', typ: 'JWT' })).toString('base64url')}.${Buffer.from(JSON.stringify({ iss: AUTHORITY, aud: 'monitoring-api', sub: 'x', exp: Math.floor(Date.now() / 1000) + 300 })).toString('base64url')}.c2ln`;
  expect(await apiStatus(unknownKey)).toBe(401);
  // The failed refresh did not erase what the API trusts: a token signed with a known key is still accepted while the provider is unreachable.
  expect(await apiStatus(token)).toBe(200);

  docker('start', container);
  await expect.poll(async () => {
    try { return await discoveryStatus(); } catch { return 0; }
  }, { timeout: 120_000, intervals: [3_000] }).toBe(200);
  expect(await apiStatus(token)).toBe(200);
  const fresh = await mintToken(browser, 'analista');
  expect(await apiStatus(fresh)).toBe(200);
});
