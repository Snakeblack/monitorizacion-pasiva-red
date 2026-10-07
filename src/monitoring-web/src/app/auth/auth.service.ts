import { computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { AuthConfig, safeReturnUrl } from './auth-config';
import { Claims, decodeClaims, Role, rolesOf } from './claims';
import { codeChallenge, randomUrlSafe } from './pkce';

export type AuthErrorCode =
  | 'configuration'
  | 'discovery-failed'
  | 'no-pending-login'
  | 'state-mismatch'
  | 'provider-error'
  | 'token-exchange-failed'
  | 'id-token-invalid';

// Codes are stable and the messages generic: nothing the provider or the user agent sent is ever echoed.
export class AuthError extends Error {
  constructor(readonly code: AuthErrorCode) {
    super(`Authentication failed (${code}).`);
  }
}

export type EndReason = 'expired' | 'rejected' | 'logout';

export interface AuthSession {
  subject: string;
  // Display name from the token (`preferred_username`), for the interface only; authorization never reads it.
  name?: string;
  roles: Role[];
  expiresAt: number;
}

// Everything the service needs from the browser, injectable so the flow is testable without one.
export interface AuthPlatform {
  fetch: typeof fetch;
  storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
  navigate: (url: string) => void;
  origin: string;
  now: () => number;
  setTimer: (callback: () => void, ms: number) => () => void;
}

export const AUTH_PLATFORM = new InjectionToken<AuthPlatform>('AUTH_PLATFORM', {
  providedIn: 'root',
  factory: () => ({
    fetch: (input, init) => fetch(input, init),
    storage: sessionStorage,
    navigate: (url) => location.assign(url),
    origin: location.origin,
    now: () => Date.now(),
    setTimer: (callback, ms) => {
      const handle = setTimeout(callback, ms);
      return () => clearTimeout(handle);
    },
  }),
});

// Holds the validated runtime configuration; filled once at start-up (see provideAuth).
@Injectable({ providedIn: 'root' })
export class AuthConfigStore {
  config: AuthConfig | null = null;
  // True when a configuration was expected but could not be read or validated: access is then refused, never opened.
  failed = false;
}

interface Discovery {
  authorization: string;
  token: string;
  endSession: string | null;
}

interface Pending {
  state: string;
  nonce: string;
  verifier: string;
  returnUrl: string;
}

const PENDING_KEY = 'monitoring.auth.pending';
const REDIRECT_PATH = '/auth/callback';
const SIGNED_OUT_PATH = '/signed-out';
// The session ends slightly before the token does, so a request is never sent with a token about to lapse.
const EARLY_END_MS = 10_000;
const MAXIMUM_LIFETIME_SECONDS = 86_400;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly store = inject(AuthConfigStore);
  private readonly platform = inject(AUTH_PLATFORM);
  private readonly state = signal<AuthSession | null>(null);
  private readonly ended = signal<EndReason | null>(null);
  private token: string | null = null;
  private idToken: string | null = null;
  private cancelTimer: (() => void) | null = null;
  private discovery: Discovery | null = null;

  readonly session = this.state.asReadonly();
  readonly authenticated = computed(() => this.state() !== null);
  readonly endedReason = this.ended.asReadonly();
  readonly roles = computed(() => this.state()?.roles ?? []);

  get enabled(): boolean {
    return this.store.config !== null;
  }

  get configurationFailed(): boolean {
    return this.store.failed;
  }

  // The bearer token for API calls, or null (and the session ended) once it is past its expiry.
  accessToken(): string | null {
    const session = this.state();
    if (session === null) return null;
    if (this.platform.now() >= session.expiresAt) {
      this.expire('expired');
      return null;
    }
    return this.token;
  }

  async startLogin(returnUrl: string): Promise<void> {
    const config = this.requireConfig();
    const discovery = await this.discover(config);
    const pending: Pending = {
      state: randomUrlSafe(32),
      nonce: randomUrlSafe(32),
      verifier: randomUrlSafe(64),
      returnUrl: safeReturnUrl(returnUrl),
    };
    this.platform.storage.setItem(PENDING_KEY, JSON.stringify(pending));
    const url = new URL(discovery.authorization);
    url.search = new URLSearchParams({
      response_type: 'code',
      client_id: config.clientId,
      redirect_uri: `${this.platform.origin}${REDIRECT_PATH}`,
      scope: config.scope,
      state: pending.state,
      nonce: pending.nonce,
      code_challenge: await codeChallenge(pending.verifier),
      code_challenge_method: 'S256',
    }).toString();
    this.platform.navigate(url.toString());
  }

  // Completes the redirect: validates state, exchanges the code with the PKCE verifier, validates the ID token and opens the
  // session. The one-time pending record is removed first, so a callback can never be completed twice.
  async completeLogin(search: string): Promise<string> {
    const config = this.requireConfig();
    const pending = this.takePending();
    if (pending === null) throw new AuthError('no-pending-login');
    const parameters = new URLSearchParams(search);
    if (parameters.get('state') !== pending.state) throw new AuthError('state-mismatch');
    if (parameters.has('error')) throw new AuthError('provider-error');
    const code = parameters.get('code');
    if (!code) throw new AuthError('token-exchange-failed');

    const discovery = await this.discover(config);
    const tokens = await this.exchange(config, discovery, code, pending.verifier);
    const idClaims = decodeClaims(tokens.idToken);
    if (!this.validIdToken(idClaims, config, pending.nonce)) throw new AuthError('id-token-invalid');
    const accessClaims = decodeClaims(tokens.accessToken);
    const subject = typeof accessClaims?.['sub'] === 'string' ? accessClaims['sub'] : (idClaims!['sub'] as string);

    this.token = tokens.accessToken;
    this.idToken = tokens.idToken;
    const expiresAt = this.platform.now() + Math.max(0, tokens.lifetimeSeconds * 1000 - EARLY_END_MS);
    this.ended.set(null);
    const name = typeof accessClaims?.['preferred_username'] === 'string' ? accessClaims['preferred_username'] : undefined;
    this.state.set({ subject, name, roles: rolesOf(accessClaims), expiresAt });
    this.cancelTimer?.();
    this.cancelTimer = this.platform.setTimer(() => this.accessToken(), Math.max(0, expiresAt - this.platform.now()));
    return safeReturnUrl(pending.returnUrl);
  }

  // Ends the local session. The first reason wins: a rejection by the API is not later reported as an expiry.
  expire(reason: EndReason): void {
    if (this.state() === null) return;
    this.cancelTimer?.();
    this.cancelTimer = null;
    this.token = null;
    this.state.set(null);
    this.ended.set(reason);
  }

  logout(): void {
    const idToken = this.idToken;
    this.expire('logout');
    this.idToken = null;
    const config = this.store.config;
    const endSession = this.discovery?.endSession;
    if (config === null || !endSession || idToken === null) return;
    const url = new URL(endSession);
    url.search = new URLSearchParams({
      client_id: config.clientId,
      id_token_hint: idToken,
      post_logout_redirect_uri: `${this.platform.origin}${SIGNED_OUT_PATH}`,
    }).toString();
    this.platform.navigate(url.toString());
  }

  private requireConfig(): AuthConfig {
    if (this.store.config === null) throw new AuthError('configuration');
    return this.store.config;
  }

  private takePending(): Pending | null {
    const raw = this.platform.storage.getItem(PENDING_KEY);
    this.platform.storage.removeItem(PENDING_KEY);
    if (raw === null) return null;
    try {
      const value: unknown = JSON.parse(raw);
      const pending = value as Partial<Pending> | null;
      return typeof pending?.state === 'string' && typeof pending.nonce === 'string' && typeof pending.verifier === 'string'
        && typeof pending.returnUrl === 'string'
        ? (pending as Pending)
        : null;
    } catch {
      return null;
    }
  }

  private async discover(config: AuthConfig): Promise<Discovery> {
    if (this.discovery !== null) return this.discovery;
    let document: Record<string, unknown>;
    try {
      const response = await this.platform.fetch(`${config.authority}/.well-known/openid-configuration`, { credentials: 'omit', cache: 'no-store' });
      if (!response.ok) throw new AuthError('discovery-failed');
      document = (await response.json()) as Record<string, unknown>;
    } catch {
      throw new AuthError('discovery-failed');
    }
    // The document must come from, and name, the configured issuer; endpoints must be as secure as the authority itself.
    const authority = new URL(config.authority);
    const endpoint = (name: string, required: boolean): string | null => {
      const value = document[name];
      if (value === undefined && !required) return null;
      if (typeof value !== 'string') throw new AuthError('discovery-failed');
      const url = new URL(value);
      if (url.protocol !== authority.protocol || url.host !== authority.host) throw new AuthError('discovery-failed');
      return url.toString();
    };
    try {
      if (document['issuer'] !== config.authority) throw new AuthError('discovery-failed');
      this.discovery = { authorization: endpoint('authorization_endpoint', true)!, token: endpoint('token_endpoint', true)!, endSession: endpoint('end_session_endpoint', false) };
    } catch {
      throw new AuthError('discovery-failed');
    }
    return this.discovery;
  }

  private async exchange(config: AuthConfig, discovery: Discovery, code: string, verifier: string)
    : Promise<{ accessToken: string; idToken: string; lifetimeSeconds: number }> {
    try {
      const response = await this.platform.fetch(discovery.token, {
        method: 'POST',
        credentials: 'omit',
        cache: 'no-store',
        headers: { 'content-type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type: 'authorization_code',
          code,
          redirect_uri: `${this.platform.origin}${REDIRECT_PATH}`,
          client_id: config.clientId,
          code_verifier: verifier,
        }).toString(),
      });
      if (!response.ok) throw new AuthError('token-exchange-failed');
      const body = (await response.json()) as Record<string, unknown>;
      const lifetime = body['expires_in'];
      if (typeof body['token_type'] !== 'string' || body['token_type'].toLowerCase() !== 'bearer'
        || typeof body['access_token'] !== 'string' || typeof body['id_token'] !== 'string'
        || typeof lifetime !== 'number' || !Number.isFinite(lifetime) || lifetime <= 0) {
        throw new AuthError('token-exchange-failed');
      }
      return { accessToken: body['access_token'], idToken: body['id_token'], lifetimeSeconds: Math.min(lifetime, MAXIMUM_LIFETIME_SECONDS) };
    } catch (error) {
      throw error instanceof AuthError ? error : new AuthError('token-exchange-failed');
    }
  }

  // The ID token comes straight from the token endpoint over TLS, which OIDC Core 3.1.3.7 accepts in place of a signature check;
  // its issuer, audience, nonce, subject and lifetime are still verified.
  private validIdToken(claims: Claims | null, config: AuthConfig, nonce: string): boolean {
    if (claims === null) return false;
    const audience = claims['aud'];
    const audiences = Array.isArray(audience) ? audience : [audience];
    return claims['iss'] === config.authority
      && audiences.includes(config.clientId)
      && claims['nonce'] === nonce
      && typeof claims['sub'] === 'string' && claims['sub'] !== ''
      && typeof claims['exp'] === 'number' && claims['exp'] * 1000 > this.platform.now();
  }
}
