import { decodeClaims, rolesOf } from './claims';

function jwt(payload: unknown): string {
  // Real tokens carry UTF-8 JSON, so encode the bytes (btoa alone only handles Latin-1).
  const encode = (value: unknown) =>
    btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(value)))).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
  return `${encode({ alg: 'RS256' })}.${encode(payload)}.signature`;
}

describe('claims (display only; the API validates every token)', () => {
  it('reads the claims of a well-formed token', () => {
    const claims = decodeClaims(jwt({ sub: 'user-1', roles: ['analista'], exp: 1760000000, name: 'Ñandú' }));
    expect(claims).toEqual({ sub: 'user-1', roles: ['analista'], exp: 1760000000, name: 'Ñandú' });
  });

  it('returns null for anything that is not a three-part JSON object token', () => {
    for (const bad of ['', 'a.b', 'a.b.c.d', 'not-base64.!!!.x', jwt('text'), jwt(null), jwt([1])]) {
      expect(decodeClaims(bad)).toBeNull();
    }
  });

  it('keeps only known string roles', () => {
    expect(rolesOf({ roles: ['analista', 'otro', 7, null, 'auditor'] })).toEqual(['analista', 'auditor']);
    expect(rolesOf({ roles: 'analista' })).toEqual([]);
    expect(rolesOf(null)).toEqual([]);
  });
});
