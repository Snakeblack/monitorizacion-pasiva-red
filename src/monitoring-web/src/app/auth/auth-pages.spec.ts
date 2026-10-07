import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { AuthCallbackPage, SignedOutPage } from './auth-pages';
import { AuthError, AuthService } from './auth.service';

function setup(service: Record<string, unknown>, query: Record<string, string> = {}) {
  TestBed.configureTestingModule({
    imports: [AuthCallbackPage, SignedOutPage],
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: service as unknown as AuthService },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { keys: Object.keys(query), get: (key: string) => query[key] ?? null } } } },
    ],
  });
  return TestBed.inject(Router);
}

describe('AuthCallbackPage', () => {
  it('completes the login with exactly what the provider returned and goes where the user was headed', async () => {
    const completeLogin = vi.fn().mockResolvedValue('/inventory');
    const router = setup({ completeLogin }, { code: 'abc', state: 's1', session_state: 'ignored' });
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(AuthCallbackPage);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(completeLogin).toHaveBeenCalledWith('?code=abc&state=s1&session_state=ignored');
    expect(navigate).toHaveBeenCalledWith('/inventory', { replaceUrl: true });
  });

  it('shows a generic failure with a retry and no provider detail', async () => {
    const startLogin = vi.fn().mockResolvedValue(undefined);
    setup({ completeLogin: vi.fn().mockRejectedValue(new AuthError('state-mismatch')), startLogin }, { code: 'abc', state: 'forged', error_description: 'leaky-detail' });
    const fixture = TestBed.createComponent(AuthCallbackPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No se pudo iniciar sesión');
    expect(text).not.toContain('leaky-detail');
    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
    expect(startLogin).toHaveBeenCalledWith('/');
  });
});

describe('SignedOutPage', () => {
  it.each([
    ['expired', 'caducado'],
    ['rejected', 'rechazado'],
    ['logout', 'cerrado sesión'],
    ['config', 'configuración'],
    ['unavailable', 'proveedor de identidad'],
  ])('explains %s without data', (reason, expected) => {
    setup({ enabled: true, configurationFailed: reason === 'config', startLogin: vi.fn() }, { reason });
    const fixture = TestBed.createComponent(SignedOutPage);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(expected);
  });

  it('offers to sign in again except when the configuration is invalid', () => {
    const startLogin = vi.fn().mockResolvedValue(undefined);
    setup({ enabled: true, configurationFailed: false, startLogin }, { reason: 'expired' });
    const fixture = TestBed.createComponent(SignedOutPage);
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
    expect(startLogin).toHaveBeenCalledWith('/');
    TestBed.resetTestingModule();
    setup({ enabled: false, configurationFailed: true, startLogin }, { reason: 'config' });
    const closed = TestBed.createComponent(SignedOutPage);
    closed.detectChanges();
    expect((closed.nativeElement as HTMLElement).querySelector('button')).toBeNull();
  });
});
