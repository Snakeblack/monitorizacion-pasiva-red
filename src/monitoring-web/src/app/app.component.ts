import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService, AuthSession } from './auth/auth.service';
import { Role } from './auth/claims';
import { Icon } from './ui/icon.component';

const ROLE_LABELS: Record<Role, string> = {
  analista: 'Analista',
  auditor: 'Auditor',
  'administrador-inventario': 'Administrador de inventario',
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.component.css',
  templateUrl: './app.component.html',
})
export class App {
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly navOpen = signal(true);
  protected readonly navToggleLabel = computed(() =>
    this.navOpen() ? 'Ocultar navegación' : 'Mostrar navegación',
  );

  constructor() {
    // When the session ends (expiry, rejection by the API or logout) the work surfaces are destroyed by leaving them, which discards
    // every result and cursor they held; only the explanation page remains.
    effect(() => {
      const reason = this.auth.endedReason();
      if (reason !== null) void this.router.navigate(['/signed-out'], { queryParams: { reason } });
    });
  }

  protected toggleNav(): void {
    this.navOpen.update((open) => !open);
  }

  protected logout(): void {
    this.auth.logout();
  }

  protected initial(session: AuthSession): string {
    return (session.name ?? session.subject).charAt(0);
  }

  protected roleLabels(session: AuthSession): string {
    return session.roles.map((role) => ROLE_LABELS[role]).join(' · ');
  }
}
