import { TestBed } from '@angular/core/testing';
import { AuthConfigStore, AUTH_PLATFORM, AuthError, AuthPlatform, AuthService } from './auth.service';
import { codeChallenge } from './pkce';

const AUTHORITY = 'https://idp.example/realms/monitoring';
const ORIGIN = 'https://app.example';
const NOW = Date.parse('2026-10-06T12:00:00Z');

function encode(value: unknown): string {
  return btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(value)))).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}
function jwt(payload: Record<string, unknown>): string {
  return `${encode({ alg: 'RS256' })}.${encode(payload)}.signature`;
}

const DISCOVERY = {
  issuer: AUTHORITY,
  authorization_endpoint: `${AUTHORITY}/protocol/openid-connect/auth`,
  token_endpoint: `${AUTHORITY}/protocol/openid-connect/token`,
  end_session_endpoint: `${AUTHORITY}/protocol/openid-connect/logout`,
};

interface Rig {
  platform: AuthPlatform;
  service: AuthService;
  navigations: string[];
  requests: { url: string; init?: RequestInit }[];
  timers: { callback: () => void; ms: number; cancelled: boolean }[];
  storage: Map<string, string>;
  clock: { now: number };
  respond: { discovery: () => Response | Promise<Response>; token: () => Response | Promise<Response> };
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

function rig(config: { authority: string; clientId: string; scope: string } | null = { authority: AUTHORITY, clientId: 'monitoring-web', scope: 'openid' }): Rig {
  const navigations: string[] = [];
  const requests: Rig['requests'] = [];
  const timers: Rig['timers'] = [];
  const storage = new Map<string, string>();
  const clock = { now: NOW };
  const respond = { discovery: () => json(DISCOVERY), token: () => json({}) };
  const platform: AuthPlatform = {
    fetch: async (url, init) => {
      requests.push({ url: String(url), init });
      return String(url).endsWith('/.well-known/openid-configuration') ? respond.discovery() : respond.token();
    },
    storage: {
      getItem: (key) => storage.get(key) ?? null,
      setItem: (key, value) => void storage.set(key, value),
      removeItem: (key) => void storage.delete(key),
    },
    navigate: (url) => void navigations.push(url),
    origin: ORIGIN,
    now: () => clock.now,
    setTimer: (callback, ms) => {
      const timer = { callback, ms, cancelled: false };
      timers.push(timer);
      return () => (timer.cancelled = true);
    },
  };
  TestBed.configureTestingModule({ providers: [{ provide: AUTH_PLATFORM, useValue: platform }] });
  TestBed.inject(AuthConfigStore).config = config;
  return { platform, service: TestBed.inject(AuthService), navigations, requests, timers, storage, clock, respond };
}

function validTokens(nonce: string, overrides: Record<string, unknown> = {}, expiresIn = 300): Response {
  const seconds = NOW / 1000;
  return json({
    token_type: 'Bearer',
    access_token: jwt({ sub: 'user-1', roles: ['analista', 'administrador-inventario'], exp: seconds + expiresIn }),
    id_token: jwt({ iss: AUTHORITY, aud: 'monitoring-web', sub: 'user-1', nonce, exp: seconds + expiresIn, ...overrides }),
    expires_in: expiresIn,
  });
}

async function begin(r: Rig, returnUrl = '/sessions'): Promise<{ state: string; nonce: string; verifier: string; url: URL }> {
  await r.service.startLogin(returnUrl);
  const url = new URL(r.navigations[0]);
  const pending = JSON.parse([...r.storage.values()][0]) as { verifier: string };
  return { state: url.searchParams.get('state')!, nonce: url.searchParams.get('nonce')!, verifier: pending.verifier, url };
}

describe('AuthService', () => {
  it('is disabled without configuration and never touches the network or storage', async () => {
    const r = rig(null);
    expect(r.service.enabled).toBe(false);
    expect(r.service.authenticated()).toBe(false);
    expect(r.requests).toEqual([]);
    expect(r.storage.size).toBe(0);
  });

  it('starts an authorization code flow with PKCE S256, state and nonce, keeping the verifier out of the URL', async () => {
    const r = rig();
    const { url, verifier, state, nonce } = await begin(r);
    expect(r.navigations).toHaveLength(1);
    expect(`${url.origin}${url.pathname}`).toBe(DISCOVERY.authorization_endpoint);
    expect(Object.fromEntries(url.searchParams)).toEqual({
      response_type: 'code',
      client_id: 'monitoring-web',
      redirect_uri: `${ORIGIN}/auth/callback`,
      scope: 'openid',
      state,
      nonce,
      code_challenge: await codeChallenge(verifier),
      code_challenge_method: 'S256',
    });
    expect(url.toString()).not.toContain(verifier);
    expect(state).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(verifier).toMatch(/^[A-Za-z0-9_-]{86}$/);
  });

  it('refuses a provider whose discovery document is inconsistent or insecure, without redirecting', async () => {
    for (const document of [
      { ...DISCOVERY, issuer: 'https://evil.example/realms/monitoring' },
      { ...DISCOVERY, authorization_endpoint: 'http://idp.example/auth' },
      { ...DISCOVERY, token_endpoint: 'https://other.example/token' },
      { ...DISCOVERY, token_endpoint: undefined },
    ]) {
      TestBed.resetTestingModule();
      const r = rig();
      r.respond.discovery = () => json(document);
      await expect(r.service.startLogin('/')).rejects.toBeInstanceOf(AuthError);
      expect(r.navigations).toEqual([]);
    }
  });

  it('exchanges the code with the verifier and no secret, then holds the session in memory only', async () => {
    const r = rig();
    const { state, nonce, verifier } = await begin(r, '/inventory');
    r.respond.token = () => validTokens(nonce);
    const destination = await r.service.completeLogin(`?code=abc&state=${state}`);
    expect(destination).toBe('/inventory');
    const exchange = r.requests.find((request) => request.url === DISCOVERY.token_endpoint)!;
    expect(exchange.init?.method).toBe('POST');
    expect(Object.fromEntries(new URLSearchParams(String(exchange.init?.body)))).toEqual({
      grant_type: 'authorization_code',
      code: 'abc',
      redirect_uri: `${ORIGIN}/auth/callback`,
      client_id: 'monitoring-web',
      code_verifier: verifier,
    });
    expect(r.service.authenticated()).toBe(true);
    expect(r.service.session()?.subject).toBe('user-1');
    expect(r.service.session()?.roles).toEqual(['analista', 'administrador-inventario']);
    expect(r.service.accessToken()).toMatch(/^eyJ|^[A-Za-z0-9_-]+\./);
    // Nothing sensitive is left in storage and the one-time login record is gone.
    expect(r.storage.size).toBe(0);
  });

  it('cannot complete the same callback twice (the pending record is single-use)', async () => {
    const r = rig();
    const { state, nonce } = await begin(r);
    r.respond.token = () => validTokens(nonce);
    await r.service.completeLogin(`?code=abc&state=${state}`);
    r.service.expire('logout');
    await expect(r.service.completeLogin(`?code=abc&state=${state}`)).rejects.toMatchObject({ code: 'no-pending-login' });
  });

  it('rejects a callback whose state differs and never contacts the token endpoint', async () => {
    const r = rig();
    await begin(r);
    const before = r.requests.length;
    await expect(r.service.completeLogin('?code=abc&state=forged')).rejects.toMatchObject({ code: 'state-mismatch' });
    expect(r.requests.length).toBe(before);
    expect(r.storage.size).toBe(0);
    expect(r.service.authenticated()).toBe(false);
  });

  it('reports a provider error without echoing what the provider sent', async () => {
    const r = rig();
    const { state } = await begin(r);
    const failure = await r.service.completeLogin(`?error=access_denied&error_description=secret+detail&state=${state}`).catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(AuthError);
    expect((failure as AuthError).code).toBe('provider-error');
    expect(String((failure as AuthError).message)).not.toContain('secret');
  });

  it.each([
    ['wrong nonce', { nonce: 'other' }],
    ['wrong audience', { aud: 'another-client' }],
    ['wrong issuer', { iss: 'https://evil.example/realms/monitoring' }],
    ['expired', { exp: NOW / 1000 - 10 }],
    ['no subject', { sub: undefined }],
  ])('rejects an id token with %s', async (_label, override) => {
    const r = rig();
    const { state, nonce } = await begin(r);
    r.respond.token = () => validTokens(nonce, override);
    await expect(r.service.completeLogin(`?code=abc&state=${state}`)).rejects.toMatchObject({ code: 'id-token-invalid' });
    expect(r.service.authenticated()).toBe(false);
  });

  it.each([
    ['not a bearer token', { token_type: 'mac' }],
    ['no access token', { access_token: undefined }],
    ['no id token', { id_token: undefined }],
    ['invalid lifetime', { expires_in: -1 }],
    ['non numeric lifetime', { expires_in: 'soon' }],
  ])('rejects a token response that is %s', async (_label, override) => {
    const r = rig();
    const { state, nonce } = await begin(r);
    r.respond.token = async () => {
      const base = (await validTokens(nonce).json()) as Record<string, unknown>;
      return json({ ...base, ...override });
    };
    await expect(r.service.completeLogin(`?code=abc&state=${state}`)).rejects.toMatchObject({ code: 'token-exchange-failed' });
    expect(r.service.authenticated()).toBe(false);
  });

  it('rejects a failing token endpoint', async () => {
    const r = rig();
    const { state } = await begin(r);
    r.respond.token = () => json({ error: 'invalid_grant' }, 400);
    await expect(r.service.completeLogin(`?code=abc&state=${state}`)).rejects.toMatchObject({ code: 'token-exchange-failed' });
  });

  it('ends the session when the token expires, clearing the token and recording why', async () => {
    const r = rig();
    const { state, nonce } = await begin(r);
    r.respond.token = () => validTokens(nonce, {}, 300);
    await r.service.completeLogin(`?code=abc&state=${state}`);
    const timer = r.timers.at(-1)!;
    expect(timer.ms).toBe(290_000); // 10 s before expiry, never after
    r.clock.now += 291_000;
    timer.callback();
    expect(r.service.authenticated()).toBe(false);
    expect(r.service.accessToken()).toBeNull();
    expect(r.service.endedReason()).toBe('expired');
  });

  it('never hands out a token past its expiry even if the timer has not fired yet', async () => {
    const r = rig();
    const { state, nonce } = await begin(r);
    r.respond.token = () => validTokens(nonce, {}, 300);
    await r.service.completeLogin(`?code=abc&state=${state}`);
    r.clock.now += 295_000;
    expect(r.service.accessToken()).toBeNull();
    expect(r.service.endedReason()).toBe('expired');
  });

  it('records a rejection by the API once and ignores further ones', async () => {
    const r = rig();
    const { state, nonce } = await begin(r);
    r.respond.token = () => validTokens(nonce);
    await r.service.completeLogin(`?code=abc&state=${state}`);
    r.service.expire('rejected');
    r.service.expire('expired');
    expect(r.service.endedReason()).toBe('rejected');
    expect(r.timers.at(-1)!.cancelled).toBe(true);
  });

  it('logs out locally and at the provider with the id token hint and a same-origin return page', async () => {
    const r = rig();
    const { state, nonce } = await begin(r);
    r.respond.token = () => validTokens(nonce);
    await r.service.completeLogin(`?code=abc&state=${state}`);
    r.navigations.length = 0;
    r.service.logout();
    expect(r.service.authenticated()).toBe(false);
    expect(r.service.endedReason()).toBe('logout');
    const target = new URL(r.navigations[0]);
    expect(`${target.origin}${target.pathname}`).toBe(DISCOVERY.end_session_endpoint);
    expect(target.searchParams.get('client_id')).toBe('monitoring-web');
    expect(target.searchParams.get('post_logout_redirect_uri')).toBe(`${ORIGIN}/signed-out`);
    expect(target.searchParams.get('id_token_hint')).toMatch(/^[\w-]+\.[\w-]+\.[\w-]+$/);
  });

  it('sanitizes the post-login destination', async () => {
    const r = rig();
    const { state, nonce } = await begin(r, '//evil.example');
    r.respond.token = () => validTokens(nonce);
    expect(await r.service.completeLogin(`?code=abc&state=${state}`)).toBe('/');
  });
});
