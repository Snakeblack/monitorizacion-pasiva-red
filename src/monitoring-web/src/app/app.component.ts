import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
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
}
