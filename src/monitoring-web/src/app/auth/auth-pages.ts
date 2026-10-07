import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from './auth.service';

const MESSAGE_STYLES = `
  .auth-message { max-width: 40rem; margin: var(--ui-space-4, 1.5rem) auto; padding: var(--ui-space-3, 1rem); }
  .auth-message h1 { margin: 0 0 var(--ui-space-2, .5rem); font-size: var(--ui-font-size-title, 1.25rem); }
  .auth-message button { height: var(--ui-control, 2.25rem); padding: 0 var(--ui-space-3, 1rem); cursor: pointer; font: inherit; }
`;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-auth-callback',
  styles: MESSAGE_STYLES,
  template: `
    @if (failed()) {
      <section class="auth-message" role="alert">
        <h1>No se pudo iniciar sesión</h1>
        <p>La respuesta del proveedor de identidad no es válida o caducó. Vuelve a intentarlo.</p>
        <button type="button" (click)="retry()">Iniciar sesión</button>
      </section>
    } @else {
      <p role="status">Completando el inicio de sesión…</p>
    }
  `,
})
export class AuthCallbackPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly failed = signal(false);

  async ngOnInit(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    const search = new URLSearchParams(params.keys.map((key) => [key, params.get(key) ?? ''])).toString();
    try {
      const destination = await this.auth.completeLogin(`?${search}`);
      await this.router.navigateByUrl(destination, { replaceUrl: true });
    } catch {
      this.failed.set(true);
    }
  }

  protected retry(): void {
    void this.auth.startLogin('/').catch(() => this.failed.set(true));
  }
}

const REASONS: Record<string, string> = {
  expired: 'Tu sesión ha caducado. Inicia sesión de nuevo para continuar; se han descartado los resultados en pantalla.',
  rejected: 'El servidor ha rechazado tu sesión. Inicia sesión de nuevo; se han descartado los resultados en pantalla.',
  logout: 'Has cerrado sesión. Se han descartado los resultados en pantalla.',
  config: 'La configuración de autenticación no es válida y la aplicación está cerrada. Contacta con el administrador.',
  unavailable: 'No se pudo contactar con el proveedor de identidad. Reintenta en unos minutos.',
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-signed-out',
  styles: MESSAGE_STYLES,
  template: `
    <section class="auth-message">
      <h1>Sesión no iniciada</h1>
      <p role="status">{{ message }}</p>
      @if (canSignIn) {
        <button type="button" (click)="signIn()">Iniciar sesión</button>
      }
    </section>
  `,
})
export class SignedOutPage {
  private readonly auth = inject(AuthService);
  private readonly reason = inject(ActivatedRoute).snapshot.queryParamMap.get('reason') ?? '';
  protected readonly message = REASONS[this.reason] ?? 'No has iniciado sesión.';
  protected readonly canSignIn = !this.auth.configurationFailed;

  protected signIn(): void {
    void this.auth.startLogin('/').catch(() => undefined);
  }
}
