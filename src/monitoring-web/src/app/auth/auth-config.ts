export interface AuthConfig {
  // Issuer of the identity provider; discovery is read from <authority>/.well-known/openid-configuration.
  authority: string;
  clientId: string;
  scope: string;
}

const LOOPBACK = new Set(['localhost', '127.0.0.1', '[::1]']);

// Validates the runtime configuration. `{ enabled: false }` means "no authentication" (the API's development mode); anything
// else must be complete and secure or it is rejected, so a typo can never silently downgrade a production deployment.
export function parseAuthConfig(raw: unknown): AuthConfig | null {
  if (typeof raw !== 'object' || raw === null) throw new Error('Invalid authentication configuration.');
  const value = raw as Record<string, unknown>;
  if (value['enabled'] === false) return null;
  if (value['enabled'] !== true) throw new Error('Invalid authentication configuration.');
  const { authority, clientId } = value;
  if (typeof authority !== 'string' || typeof clientId !== 'string' || clientId.trim() === '') {
    throw new Error('Invalid authentication configuration.');
  }
  let url: URL;
  try {
    url = new URL(authority);
  } catch {
    throw new Error('Invalid authentication configuration.');
  }
  const secure = url.protocol === 'https:' || (url.protocol === 'http:' && LOOPBACK.has(url.hostname));
  if (!secure || url.username !== '' || url.password !== '' || url.search !== '' || url.hash !== '') {
    throw new Error('Invalid authentication configuration.');
  }
  const scope = value['scope'] === undefined ? 'openid' : value['scope'];
  if (typeof scope !== 'string' || !scope.split(' ').includes('openid')) {
    throw new Error('Invalid authentication configuration.');
  }
  return { authority: authority.replace(/\/+$/, ''), clientId, scope };
}

// A post-login destination must be a path of this application: never another origin, protocol-relative or with control characters.
export function safeReturnUrl(candidate: string | null | undefined): string {
  if (typeof candidate !== 'string' || !candidate.startsWith('/') || candidate.startsWith('//')) return '/';
  if (candidate.includes('\\') || /[\u0000-\u001f\u007f]/.test(candidate)) return '/';
  return candidate;
}
