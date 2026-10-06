import { TestBed } from '@angular/core/testing';
import { loadAuthConfig } from './auth-config-loader';
import { AuthConfigStore } from './auth.service';

function load(response: () => Promise<Response>) {
  TestBed.configureTestingModule({});
  const store = TestBed.inject(AuthConfigStore);
  return loadAuthConfig(store, response as unknown as typeof fetch).then(() => store);
}

describe('loadAuthConfig', () => {
  it('loads a valid enabled configuration', async () => {
    const store = await load(async () => Response.json({ enabled: true, authority: 'https://idp.example/realms/m', clientId: 'web' }));
    expect(store.config).toEqual({ authority: 'https://idp.example/realms/m', clientId: 'web', scope: 'openid' });
    expect(store.failed).toBe(false);
  });

  it('treats an explicit disabled configuration as development, not as a failure', async () => {
    const store = await load(async () => Response.json({ enabled: false }));
    expect(store).toMatchObject({ config: null, failed: false });
  });

  it.each([
    ['a missing file', async () => new Response('', { status: 404 })],
    ['a server error', async () => new Response('', { status: 500 })],
    ['an unreachable server', async () => Promise.reject(new TypeError('network'))],
    ['malformed JSON', async () => new Response('{nope')],
    ['an insecure authority', async () => Response.json({ enabled: true, authority: 'http://idp.example', clientId: 'web' })],
  ])('fails closed on %s', async (_label, response) => {
    const store = await load(response);
    expect(store).toMatchObject({ config: null, failed: true });
  });
});
