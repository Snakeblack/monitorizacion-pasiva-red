import { parseAuthConfig } from './auth-config';
import { AuthConfigStore } from './auth.service';

// Reads /auth-config.json once at start-up. The file must exist and be valid: a missing, unreachable or malformed one marks the
// configuration as failed, which closes the application (see authGuard) instead of silently running without authentication.
// Development ships `{ "enabled": false }`; a deployment provides its own file.
export async function loadAuthConfig(store: AuthConfigStore, fetcher: typeof fetch = (input, init) => fetch(input, init)): Promise<void> {
  try {
    const response = await fetcher('/auth-config.json', { cache: 'no-store', credentials: 'omit' });
    if (!response.ok) throw new Error('unavailable');
    store.config = parseAuthConfig(await response.json());
    store.failed = false;
  } catch {
    store.config = null;
    store.failed = true;
  }
}
