import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app.component';
import { AuthService } from './auth/auth.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('should toggle navigation with a native button', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const toggle = compiled.querySelector('button[aria-controls="app-nav"]');

    expect(toggle).toBeInstanceOf(HTMLButtonElement);
    expect((toggle as HTMLButtonElement).type).toBe('button');
    expect(toggle?.getAttribute('aria-expanded')).toBe('true');

    (toggle as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
    expect(toggle?.getAttribute('aria-label')).toBe('Mostrar navegación');
  });

  it('navigates to the sessions work surface using a native named link', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const sessions = Array.from(compiled.querySelectorAll('a')).find(
      (link) => link.textContent?.trim() === 'Sesiones',
    );

    expect(sessions).toBeInstanceOf(HTMLAnchorElement);
    expect(sessions?.getAttribute('href')).toBe('/sessions');
  });
});

describe('App session handling', () => {
  function setup(auth: Record<string, unknown>) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth as unknown as AuthService }],
    });
    return TestBed.inject(Router);
  }

  it('shows who is signed in and signs out with a native button', async () => {
    const logout = vi.fn();
    setup({ session: signal({ subject: 'user-1', roles: [], expiresAt: 0 }), endedReason: signal(null), logout });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.app-user')?.textContent).toContain('user-1');
    const button = Array.from(root.querySelectorAll('button')).find((candidate) => candidate.textContent?.trim() === 'Cerrar sesión');
    expect(button?.type).toBe('button');
    button!.click();
    expect(logout).toHaveBeenCalled();
  });

  it('shows no user controls when nobody is signed in', async () => {
    setup({ session: signal(null), endedReason: signal(null) });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.app-user')).toBeNull();
  });

  it('leaves the work surfaces for the explanation page when the session ends', async () => {
    const ended = signal<string | null>(null);
    const router = setup({ session: signal(null), endedReason: ended });
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(navigate).not.toHaveBeenCalled();
    ended.set('expired');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith(['/signed-out'], { queryParams: { reason: 'expired' } });
  });
});

describe('App navigation', () => {
  it('links to the inventory work surface with a native named link', async () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [App], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const link = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('a')).find((candidate) => candidate.textContent?.trim() === 'Inventario');
    expect(link?.getAttribute('href')).toBe('/inventory');
  });
});
