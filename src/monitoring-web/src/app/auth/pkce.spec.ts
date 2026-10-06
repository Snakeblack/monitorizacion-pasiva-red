import { codeChallenge, randomUrlSafe } from './pkce';

describe('PKCE', () => {
  it('derives the S256 challenge exactly as RFC 7636 appendix B', async () => {
    expect(await codeChallenge('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk')).toBe(
      'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM',
    );
  });

  it('generates URL-safe unpadded random values of the requested entropy that never repeat', () => {
    const values = new Set(Array.from({ length: 50 }, () => randomUrlSafe(32)));
    expect(values.size).toBe(50);
    for (const value of values) {
      expect(value).toMatch(/^[A-Za-z0-9_-]{43}$/);
    }
    expect(randomUrlSafe(64)).toMatch(/^[A-Za-z0-9_-]{86}$/);
  });
});
