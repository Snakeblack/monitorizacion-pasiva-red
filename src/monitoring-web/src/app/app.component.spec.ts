import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app.component';

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
    expect(toggle?.textContent?.trim()).toBe('Mostrar navegación');
  });

  it('should render Detalle as a native button', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const detalle = Array.from(compiled.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Detalle',
    );

    expect(detalle).toBeInstanceOf(HTMLButtonElement);
    expect(detalle?.type).toBe('button');
    expect(detalle?.getAttribute('aria-current')).toBe('page');
  });
});
