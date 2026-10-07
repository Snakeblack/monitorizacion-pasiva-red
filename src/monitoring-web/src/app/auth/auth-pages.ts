import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Icon } from '../ui/icon.component';
import { AuthService } from './auth.service';

const MESSAGE_STYLES = `
  :host { display: grid; min-height: 60vh; place-items: center; }
  .auth-message { display: flex; max-width: 28rem; flex-direction: column; align-items: flex-start; gap: var(--ui-space-3);
    padding: var(--ui-space-6); border: 1px solid var(--ui-border-subtle); border-radius: var(--ui-radius-md); background: var(--ui-surface-1); }
  .auth-icon { display: grid; width: 36px; height: 36px; place-items: center; border-radius: var(--ui-radius-sm);
    background: var(--ui-accent-soft); color: var(--ui-accent-strong); }
  .auth-message h1 { margin: 0; font-size: var(--ui-font-size-title); }
  .auth-message p { margin: 0; color: var(--ui-text-muted); line-height: 1.5; }
  .auth-message .auth-error { display: flex; gap: var(--ui-space-2); padding: var(--ui-space-3); border-radius: var(--ui-radius-sm);
    background: var(--ui-danger-soft); color: var(--ui-text); }
  .auth-status { color: var(--ui-text-muted); }
`;

// Said when the provider cannot be contacted, so the button never fails silently. It names the usual causes without exposing any detail of
// the failed request.
const UNREACHABLE =
  'No se pudo contactar con el proveedor de identidad. Comprueba que está en marcha y que la dirección de autenticación es accesible desde ' +
  'este navegador (en el laboratorio: Keycloak en http://127.0.0.1:8081; guía de demo, «Solución de problemas»).';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  selector: 'app-auth-callback',
  styles: MESSAGE_STYLES,
  template: `
    @if (failed()) {
      <section class="auth-message" role="alert">
        <span class="auth-icon"><app-icon name="alert" /></span>
        <h1>No se pudo iniciar sesión</h1>
        <p>La respuesta del proveedor de identidad no es válida o caducó. Vuelve a intentarlo.</p>
        <button type="button" class="btn btn--primary" (click)="retry()">Iniciar sesión</button>
      </section>
    } @else {
      <p role="status" class="auth-status">Completando el inicio de sesión…</p>
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
  imports: [Icon],
  selector: 'app-signed-out',
  styles: MESSAGE_STYLES,
  template: `
    <section class="auth-message">
      <span class="auth-icon"><app-icon name="shield" /></span>
      <h1>Sesión no iniciada</h1>
      <p role="status">{{ message }}</p>
      @if (unreachable()) {
        <p class="auth-error" role="alert"><app-icon name="alert" [size]="16" />{{ unreachableText }}</p>
      }
      @if (canSignIn) {
        <button type="button" class="btn btn--primary" (click)="signIn()">Iniciar sesión</button>
      }
    </section>
  `,
})
export class SignedOutPage {
  private readonly auth = inject(AuthService);
  private readonly reason = inject(ActivatedRoute).snapshot.queryParamMap.get('reason') ?? '';
  protected readonly message = REASONS[this.reason] ?? 'No has iniciado sesión.';
  protected readonly canSignIn = !this.auth.configurationFailed;
  protected readonly unreachable = signal(false);
  protected readonly unreachableText = UNREACHABLE;

  protected signIn(): void {
    this.unreachable.set(false);
    void this.auth.startLogin('/').catch(() => this.unreachable.set(true));
  }
}
