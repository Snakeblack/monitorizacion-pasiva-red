import { parseAuthConfig, safeReturnUrl } from './auth-config';

describe('parseAuthConfig', () => {
  const valid = { enabled: true, authority: 'https://idp.example/realms/monitoring', clientId: 'monitoring-web' };

  it('accepts a complete HTTPS configuration and fills safe defaults', () => {
    expect(parseAuthConfig(valid)).toEqual({
      authority: 'https://idp.example/realms/monitoring',
      clientId: 'monitoring-web',
      scope: 'openid',
    });
  });

  it('treats an explicit disabled configuration as no authentication (development)', () => {
    expect(parseAuthConfig({ enabled: false })).toBeNull();
  });

  it('rejects anything incomplete or insecure instead of guessing', () => {
    for (const bad of [
      null,
      'text',
      {},
      { enabled: true },
      { ...valid, authority: 'http://idp.example/realms/monitoring' },
      { ...valid, authority: 'idp.example' },
      { ...valid, authority: 'https://user:pw@idp.example' },
      { ...valid, clientId: '' },
      { ...valid, clientId: 42 },
      { ...valid, scope: 'profile' },
    ]) {
      expect(() => parseAuthConfig(bad)).toThrow();
    }
  });

  it('allows plain HTTP only for a loopback authority', () => {
    expect(parseAuthConfig({ ...valid, authority: 'http://localhost:8180/realms/m' })?.authority).toBe('http://localhost:8180/realms/m');
    expect(parseAuthConfig({ ...valid, authority: 'http://127.0.0.1:8180/realms/m' })?.authority).toBe('http://127.0.0.1:8180/realms/m');
  });

  it('keeps extra scopes only when openid stays present', () => {
    expect(parseAuthConfig({ ...valid, scope: 'openid monitoring' })?.scope).toBe('openid monitoring');
  });
});

describe('safeReturnUrl', () => {
  it('keeps local application paths only', () => {
    expect(safeReturnUrl('/sessions?x=1')).toBe('/sessions?x=1');
    for (const bad of ['//evil.example', 'https://evil.example', '/\\evil.example', 'javascript:alert(1)', '', null, undefined, '/a\nb']) {
      expect(safeReturnUrl(bad)).toBe('/');
    }
  });
});
