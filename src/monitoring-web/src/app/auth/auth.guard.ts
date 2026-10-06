import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Protects the work surfaces. Without authentication configured (development) it is transparent; with it, an anonymous user is
// sent to the identity provider and an invalid configuration closes the application rather than opening it.
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.configurationFailed) return router.createUrlTree(['/signed-out'], { queryParams: { reason: 'config' } });
  if (!auth.enabled || auth.authenticated()) return true;
  try {
    await auth.startLogin(state.url);
    return false;
  } catch {
    return router.createUrlTree(['/signed-out'], { queryParams: { reason: 'unavailable' } });
  }
};
