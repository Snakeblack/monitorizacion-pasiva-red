import { test, expect } from '@playwright/test';
import { admin, apiStatus, decode, mintToken } from './identity-lab.mjs';

// How long the API keeps trusting a signing key after the provider retires it (ADR-026; task 3.2). The API re-reads the key set every
// `Identity:KeysRefreshMinutes` (five by default, the library's minimum), so a retired key stops being trusted within about that time, not an hour.
// Slow by nature (about six minutes): run with E2E_SLOW=1 next to E2E_IDENTITY=1.
test.skip(process.env.E2E_IDENTITY !== '1' || process.env.E2E_SLOW !== '1', 'Needs the Keycloak overlay and E2E_SLOW=1 (it waits for the five-minute key refresh).');

test('a signing key the provider retires stops being trusted within the key refresh interval', async ({ browser }) => {
  test.setTimeout(900_000);
  // Forced key refreshes are spaced at least a minute apart; starting inside that floor would bounce the first sign-in with the new key.
  await new Promise(resolve => setTimeout(resolve, 65_000));
  const administrator = await admin();
  const original = await administrator.accessTokenLifespan();
  let key;
  try {
    // Tokens must outlive the wait, otherwise expiry would be mistaken for the key being retired.
    await administrator.setAccessTokenLifespan(1_800);
    key = await administrator.addSigningKey('lab-retired');
    const token = await mintToken(browser, 'analista');
    await expect.poll(() => apiStatus(token), { timeout: 100_000, intervals: [2_000] }).toBe(200);

    await administrator.removeSigningKey(key);
    key = undefined;
    const retired = Date.now();
    let status = 200;
    while (status === 200 && Date.now() - retired < 480_000) {
      await new Promise(resolve => setTimeout(resolve, 15_000));
      status = await apiStatus(token);
    }
    const waited = Math.round((Date.now() - retired) / 1000);
    console.log(`retired key stopped being trusted after ${waited} s (status ${status})`);
    expect(status).toBe(401);
    expect(decode(token).exp).toBeGreaterThan(Date.now() / 1000); // it was the key, not the expiry
    expect(waited).toBeLessThanOrEqual(390);
  } finally {
    if (key) await administrator.removeSigningKey(key);
    await administrator.setAccessTokenLifespan(original);
  }
});
