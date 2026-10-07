import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

function run(service: Record<string, unknown>, url = '/sessions?x=1') {
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: service as unknown as AuthService }] });
  return TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
}

describe('authGuard', () => {
  it('lets everything through when authentication is not configured (development)', async () => {
    expect(await run({ enabled: false, configurationFailed: false })).toBe(true);
  });

  it('lets an authenticated user through', async () => {
    expect(await run({ enabled: true, configurationFailed: false, authenticated: () => true })).toBe(true);
  });

  it('starts the login for an anonymous user, remembering where they were going, and blocks the route', async () => {
    const startLogin = vi.fn().mockResolvedValue(undefined);
    expect(await run({ enabled: true, configurationFailed: false, authenticated: () => false, startLogin }, '/inventory')).toBe(false);
    expect(startLogin).toHaveBeenCalledWith('/inventory');
  });

  it('sends the user to a signed-out explanation when the login cannot start', async () => {
    const startLogin = vi.fn().mockRejectedValue(new Error('discovery'));
    const result = await run({ enabled: true, configurationFailed: false, authenticated: () => false, startLogin });
    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/signed-out?reason=unavailable');
  });

  it('refuses access when the authentication configuration is invalid, instead of opening the application', async () => {
    const result = await run({ enabled: false, configurationFailed: true });
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/signed-out?reason=config');
  });
});
