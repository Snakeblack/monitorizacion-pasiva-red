import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { Icon } from '../ui/icon.component';
import { relativeTime } from '../ui/relative-time';
import {
  CandidateView, DeviceView, inventoryErrorMessage, InventoryApi, inventoryStatus, ObservationView,
} from './inventory-api';

type Tab = 'devices' | 'candidates' | 'observations';
type Action =
  | { kind: 'confirm' | 'reject' | 'merge'; candidate: CandidateView }
  | { kind: 'edit'; device: DeviceView }
  | null;

const MAXIMUM_NAME = 128;
const MAXIMUM_TEXT = 512;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  selector: 'app-inventory-page',
  styleUrl: './inventory-page.component.css',
  templateUrl: './inventory-page.component.html',
})
export class InventoryPage implements OnInit {
  private readonly api = inject(InventoryApi);
  private readonly auth = inject(AuthService);

  protected readonly tab = signal<Tab>('devices');
  protected readonly devices = signal<DeviceView[]>([]);
  protected readonly candidates = signal<CandidateView[]>([]);
  protected readonly observations = signal<ObservationView[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly action = signal<Action>(null);
  protected readonly busy = signal(false);
  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly reason = signal('');
  protected readonly mergeDeviceId = signal('');
  protected readonly maximumName = MAXIMUM_NAME;
  protected readonly maximumText = MAXIMUM_TEXT;

  // Hiding actions is a convenience only; the API checks the role and the scopes of every decision.
  protected readonly canManage = computed(() => !this.auth.enabled || this.auth.roles().includes('administrador-inventario'));
  protected readonly canSubmit = computed(() => {
    const current = this.action();
    if (current === null || this.busy()) return false;
    switch (current.kind) {
      case 'confirm':
      case 'edit':
        return this.name().trim() !== '' && this.name().length <= MAXIMUM_NAME && this.description().length <= MAXIMUM_TEXT;
      case 'reject':
        return this.reason().trim() !== '' && this.reason().length <= MAXIMUM_TEXT;
      case 'merge':
        return this.mergeDeviceId() !== '';
    }
  });

  ngOnInit(): void {
    void this.load(true);
  }

  protected since(iso: string): string {
    return relativeTime(iso, Date.now());
  }

  protected select(tab: Tab): void {
    if (tab === this.tab()) return;
    this.tab.set(tab);
    this.action.set(null);
    this.notice.set(null);
    void this.load(true);
  }

  protected more(): void {
    void this.load(false);
  }

  protected open(action: Action): void {
    this.action.set(action);
    this.notice.set(null);
    this.error.set(null);
    this.reason.set('');
    this.mergeDeviceId.set('');
    this.name.set(action?.kind === 'edit' ? action.device.name : '');
    this.description.set(action?.kind === 'edit' ? action.device.description : '');
    // Merging needs the confirmed devices to choose from.
    if (action?.kind === 'merge' && this.devices().length === 0) void this.loadDevicesForMerge();
  }

  protected cancel(): void {
    this.action.set(null);
  }

  protected deviceRevision(deviceId: string): number | null {
    return this.devices().find((device) => device.deviceId === deviceId)?.revision ?? null;
  }

  protected async submit(): Promise<void> {
    const current = this.action();
    if (current === null || !this.canSubmit()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      let done: string;
      switch (current.kind) {
        case 'confirm':
          await firstValueFrom(this.api.confirm(current.candidate.candidateId, current.candidate.revision, this.name().trim(), this.description().trim()));
          done = 'Candidato confirmado.';
          break;
        case 'reject':
          await firstValueFrom(this.api.reject(current.candidate.candidateId, current.candidate.revision, this.reason().trim()));
          done = 'Candidato rechazado.';
          break;
        case 'merge': {
          const revision = this.deviceRevision(this.mergeDeviceId());
          if (revision === null) throw new Error('unknown-device');
          await firstValueFrom(this.api.merge(current.candidate.candidateId, current.candidate.revision, this.mergeDeviceId(), revision));
          done = 'Candidato fusionado con el dispositivo.';
          break;
        }
        case 'edit':
          await firstValueFrom(this.api.updateDevice(current.device.deviceId, current.device.revision, this.name().trim(), this.description().trim()));
          done = 'Dispositivo guardado.';
          break;
      }
      this.action.set(null);
      await this.load(true);
      this.notice.set(done);
    } catch (failure) {
      const status = inventoryStatus(failure);
      this.error.set(inventoryErrorMessage(status));
      // A conflict means what is on screen is stale: show the current state again.
      if (status === 409 || status === 404) {
        this.action.set(null);
        await this.load(true, false);
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async loadDevicesForMerge(): Promise<void> {
    try {
      this.devices.set((await firstValueFrom(this.api.listDevices(null))).items);
    } catch (failure) {
      this.error.set(inventoryErrorMessage(inventoryStatus(failure)));
    }
  }

  private async load(reset: boolean, clearMessages = true): Promise<void> {
    const cursor = reset ? null : this.cursor();
    this.loading.set(true);
    if (clearMessages) {
      this.error.set(null);
      this.notice.set(null);
    }
    try {
      switch (this.tab()) {
        case 'devices': {
          const page = await firstValueFrom(this.api.listDevices(cursor));
          this.devices.update((current) => (reset ? page.items : [...current, ...page.items]));
          this.cursor.set(page.nextCursor);
          break;
        }
        case 'candidates': {
          const page = await firstValueFrom(this.api.listCandidates(null, cursor));
          this.candidates.update((current) => (reset ? page.items : [...current, ...page.items]));
          this.cursor.set(page.nextCursor);
          break;
        }
        case 'observations': {
          const page = await firstValueFrom(this.api.listObservations(cursor));
          this.observations.update((current) => (reset ? page.items : [...current, ...page.items]));
          this.cursor.set(page.nextCursor);
          break;
        }
      }
    } catch (failure) {
      const status = inventoryStatus(failure);
      // Nothing from a refused or failed read stays on screen.
      if (reset) {
        this.devices.set(this.tab() === 'devices' ? [] : this.devices());
        this.candidates.set(this.tab() === 'candidates' ? [] : this.candidates());
        this.observations.set(this.tab() === 'observations' ? [] : this.observations());
      }
      this.cursor.set(null);
      this.error.set(inventoryErrorMessage(status));
    } finally {
      this.loading.set(false);
    }
  }
}
