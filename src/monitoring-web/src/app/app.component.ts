import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.component.css',
  templateUrl: './app.component.html',
})
export class App {
  protected readonly navOpen = signal(true);
  protected readonly navToggleLabel = computed(() =>
    this.navOpen() ? 'Ocultar navegación' : 'Mostrar navegación',
  );

  protected toggleNav(): void {
    this.navOpen.update((open) => !open);
  }
}
