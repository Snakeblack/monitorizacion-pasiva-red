import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  HostListener,
  inject,
  Injector,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormField, form, max, min, required, validate } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { Icon } from '../ui/icon.component';
import { relativeTime } from '../ui/relative-time';
import { recentSessionFilters, SessionFilters, sessionFieldErrors, sessionFilterError } from './session-query';
import { normalizeUtcInput, RANGE_PRESETS, RangePresetId, rangeEndingAt } from './session-range';
import { SessionSearchApi, SessionSummary } from './session-search-api';
import { sessionErrorMessage, sessionHttpStatus } from './session-http-error';

export type FilterKey = 'siteId' | 'sensorId' | 'sourceIp' | 'destinationIp' | 'sourcePort' | 'destinationPort' | 'protocol';

interface FilterField {
  key: FilterKey;
  label: string;
  hint: string;
  placeholder: string;
  group: 'Ámbito' | 'Red';
  numeric?: boolean;
}

// What can be filtered, in the order it is offered. Every filter is an exact match; there is no free-text search by design.
export const FILTER_FIELDS: readonly FilterField[] = [
  { key: 'siteId', label: 'Sede', hint: 'Identificador exacto de la sede', placeholder: 'madrid', group: 'Ámbito' },
  { key: 'sensorId', label: 'Sonda', hint: 'Identificador exacto de la sonda', placeholder: 'sonda-1', group: 'Ámbito' },
  { key: 'sourceIp', label: 'IP origen', hint: 'Dirección IPv4 o IPv6 completa', placeholder: '192.0.2.10', group: 'Red' },
  { key: 'destinationIp', label: 'IP destino', hint: 'Dirección IPv4 o IPv6 completa', placeholder: '2001:db8::2', group: 'Red' },
  { key: 'sourcePort', label: 'Puerto origen', hint: 'Entero de 0 a 65535', placeholder: '49152', group: 'Red', numeric: true },
  { key: 'destinationPort', label: 'Puerto destino', hint: 'Entero de 0 a 65535', placeholder: '443', group: 'Red', numeric: true },
  { key: 'protocol', label: 'Protocolo', hint: 'TCP o UDP', placeholder: '', group: 'Red' },
];

const PAGE_SIZES = [25, 50, 100] as const;

// 72 s stays 72 s; beyond two minutes a number of seconds stops being readable (181255 s is two days).
function humanDuration(seconds: number): string {
  if (seconds < 120) return `${seconds} s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 120) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours} h ${minutes % 60} min`;
  return `${Math.floor(hours / 24)} d ${hours % 24} h`;
}
const UTC_SHORT = new Intl.DateTimeFormat('es-ES', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormField, Icon, RouterLink],
  selector: 'app-session-search-page',
  templateUrl: './session-search-page.component.html',
  styleUrl: './session-search-page.component.css',
})
export class SessionSearchPage {
  private readonly api = inject(SessionSearchApi);
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly cancelSearch = new Subject<void>();
  protected readonly presets = RANGE_PRESETS;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly groups = ['Ámbito', 'Red'] as const;
  protected readonly model = signal(recentSessionFilters(Date.now()));
  protected readonly filters = form(this.model, (path) => {
    required(path.from);
    required(path.to);
    min(path.pageSize, 1);
    max(path.pageSize, 100);
    validate(path, ({ value }) => {
      const message = sessionFilterError(value(), Date.now());
      return message ? { kind: 'session-query', message } : undefined;
    });
  });
  private readonly request = signal<
    { filters: SessionFilters; cursor: string; attempt: number } | undefined
  >({
    filters: this.model(),
    cursor: '',
    attempt: 0,
  });
  private attempt = 0;
  /** A preset ends "now" every time the search runs; `custom` keeps the typed dates. */
  protected readonly rangeMode = signal<RangePresetId | 'custom'>('24h');
  /** Filters the person has added; a filter stays listed while it is being typed even when still empty. */
  protected readonly shown = signal<ReadonlySet<FilterKey>>(new Set());
  protected readonly menuOpen = signal(false);
  protected readonly attempted = signal(false);
  protected readonly validation = computed(() =>
    this.attempted() ? sessionFilterError(this.model(), Date.now()) : '',
  );
  protected readonly fieldErrors = computed<Partial<Record<FilterKey, string>>>(() => sessionFieldErrors(this.model()));
  protected readonly activeFields = computed(() =>
    FILTER_FIELDS.filter((field) => this.shown().has(field.key) || this.model()[field.key] !== ''),
  );
  protected readonly availableFields = computed(() => {
    const active = new Set(this.activeFields().map((field) => field.key));
    return FILTER_FIELDS.filter((field) => !active.has(field.key));
  });
  /** What a range longer than a day still needs (sede, sonda and an address), offered as one-press additions. */
  protected readonly missingForLongRange = computed(() => {
    const filters = this.model();
    const from = Date.parse(filters.from);
    const to = Date.parse(filters.to);
    if (!Number.isFinite(from) || !Number.isFinite(to) || to - from <= 24 * 3_600_000) return [];
    const missing: FilterField[] = [];
    if (!filters.siteId) missing.push(FILTER_FIELDS[0]);
    if (!filters.sensorId) missing.push(FILTER_FIELDS[1]);
    if (!filters.sourceIp && !filters.destinationIp) missing.push(FILTER_FIELDS[2]);
    return missing;
  });
  protected readonly rangeSummary = computed(() => {
    const { from, to } = this.model();
    const start = Date.parse(from);
    const end = Date.parse(to);
    if (!Number.isFinite(start) || !Number.isFinite(end)) return '';
    return `${UTC_SHORT.format(start)} – ${UTC_SHORT.format(end)} UTC`;
  });
  private readonly results = rxResource({
    params: () => this.request(),
    stream: ({ params }) =>
      this.api.search(params.filters, params.cursor).pipe(takeUntil(this.cancelSearch)),
  });
  protected readonly loading = computed(() => this.results.isLoading());
  protected readonly page = computed(() =>
    this.results.status() === 'resolved' ? this.results.value() : undefined,
  );
  protected readonly error = computed(() =>
    this.results.status() === 'error'
      ? sessionErrorMessage(sessionHttpStatus(this.results.error()), this.auth.enabled)
      : '',
  );
  protected readonly expired = computed(
    () => this.results.status() === 'error' && sessionHttpStatus(this.results.error()) === 410,
  );
  protected readonly saturated = computed(
    () => this.results.status() === 'error' && sessionHttpStatus(this.results.error()) === 429,
  );
  protected readonly freshnessLabel = computed(() => {
    const state = this.page()?.freshness.state;
    return state ? { current: 'Índice al día', lagging: 'Índice retrasado', recovering: 'Índice en recuperación' }[state] : '';
  });
  protected readonly freshnessLag = computed(() => {
    const freshness = this.page()?.freshness;
    if (!freshness) return '';
    return freshness.lagSeconds == null ? 'Retraso desconocido' : `Retraso ${humanDuration(freshness.lagSeconds)}`;
  });
  protected readonly freshnessTone = computed(() => (this.page()?.freshness.state === 'current' ? 'success' : 'warning'));
  protected readonly freshnessMeasured = computed(() => `Medido: ${this.page()?.freshness.measuredAt ?? ''}`);
  protected readonly measuredAtClock = computed(() => (this.page()?.freshness.measuredAt ?? '').slice(11, 19));

  @HostListener('document:click', ['$event'])
  protected closeMenuOnOutsideClick(event: MouseEvent): void {
    if (this.menuOpen() && !(event.target as Element | null)?.closest('.filter-menu-anchor')) this.menuOpen.set(false);
  }

  /** "/" opens the filter menu from anywhere that is not a text field, as in the tools this interface borrows its habits from. */
  @HostListener('document:keydown', ['$event'])
  protected shortcuts(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (event.key === '/' && !event.ctrlKey && !event.metaKey && !event.altKey && !target?.closest('input, select, textarea, [contenteditable]')) {
      event.preventDefault();
      this.openMenu();
    }
  }

  protected filtersChanged(): void {
    // Angular 22 keeps an in-flight stream when params become undefined. Cancel HTTP explicitly.
    this.request.set(undefined);
    this.cancelSearch.next();
    this.attempted.set(false);
  }

  protected search(event?: Event): void {
    event?.preventDefault();
    this.menuOpen.set(false);
    this.attempted.set(true);
    const mode = this.rangeMode();
    // A preset means "up to now" at the moment of searching, not at the moment it was chosen.
    if (mode !== 'custom') this.model.update((filters) => ({ ...filters, ...rangeEndingAt(Date.now(), mode) }));
    if (sessionFilterError(this.model(), Date.now())) {
      this.request.set(undefined);
      return;
    }
    this.request.set({ filters: { ...this.model() }, cursor: '', attempt: ++this.attempt });
  }

  protected selectRange(id: RangePresetId): void {
    this.rangeMode.set(id);
    this.search();
  }

  protected customRange(): void {
    this.rangeMode.set('custom');
    this.filtersChanged();
    afterNextRender(() => this.focus('from'), { injector: this.injector });
  }

  /** Typed dates are read leniently ("2026-10-05 12:00") and stored in the canonical UTC form. */
  protected normalizeDate(key: 'from' | 'to'): void {
    const canonical = normalizeUtcInput(this.model()[key]);
    if (canonical !== null && canonical !== this.model()[key]) this.model.update((filters) => ({ ...filters, [key]: canonical }));
  }

  protected reset(): void {
    this.shown.set(new Set());
    this.model.set(recentSessionFilters(Date.now()));
    this.rangeMode.set('24h');
    this.search();
  }

  protected recent(): void {
    this.selectRange('24h');
  }

  protected openMenu(): void {
    this.menuOpen.set(true);
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('.filter-menu [role="menuitem"]')?.focus(), { injector: this.injector });
  }

  protected toggleMenu(): void {
    if (this.menuOpen()) this.menuOpen.set(false);
    else this.openMenu();
  }

  protected menuKeys(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.menuOpen.set(false);
      this.host.nativeElement.querySelector<HTMLElement>('.filter-add')?.focus();
      return;
    }
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    event.preventDefault();
    const items = [...this.host.nativeElement.querySelectorAll<HTMLElement>('.filter-menu [role="menuitem"]')];
    const index = items.indexOf(document.activeElement as HTMLElement);
    const next = event.key === 'ArrowDown' ? (index + 1) % items.length : (index - 1 + items.length) % items.length;
    items[next]?.focus();
  }

  protected addFilter(key: FilterKey): void {
    this.shown.update((current) => new Set(current).add(key));
    this.menuOpen.set(false);
    this.filtersChanged();
    afterNextRender(() => this.focus(key), { injector: this.injector });
  }

  protected removeFilter(key: FilterKey): void {
    this.shown.update((current) => {
      const next = new Set(current);
      next.delete(key);
      return next;
    });
    this.model.update((filters) => ({ ...filters, [key]: '' }));
    this.filtersChanged();
  }

  protected clearFilters(): void {
    this.shown.set(new Set());
    this.model.update((filters) => ({ ...filters, ...Object.fromEntries(FILTER_FIELDS.map((field) => [field.key, ''])) }));
    this.filtersChanged();
  }

  protected setPageSize(value: string): void {
    this.model.update((filters) => ({ ...filters, pageSize: Number(value) }));
    this.search();
  }

  protected next(): void {
    const cursor = this.page()?.nextCursor;
    const current = this.request();
    if (cursor && current && !this.loading())
      this.request.set({ ...current, cursor, attempt: ++this.attempt });
  }

  protected provenance(session: SessionSummary): string {
    if (session.provenance === 'synthetic') return 'Sintética';
    return `Capturada${session.inferred ? ' · inferida' : ''}${session.partial ? ' · parcial' : ''}`;
  }

  protected since(session: SessionSummary): string {
    return relativeTime(session.startedAt, Date.now());
  }

  /** The exact start in UTC without the date when it is today, so the table stays scannable and still auditable. */
  protected clock(session: SessionSummary): string {
    const today = new Date(Date.now()).toISOString().slice(0, 10);
    const day = session.startedAt.slice(0, 10);
    const time = session.startedAt.slice(11).replace(/Z$/, '');
    return `${day === today ? '' : day + ' '}${time} UTC`;
  }

  protected duration(session: SessionSummary): string {
    const milliseconds = Date.parse(session.endedAt) - Date.parse(session.startedAt);
    if (!Number.isFinite(milliseconds) || milliseconds < 0) return '';
    if (milliseconds < 1000) return `${milliseconds} ms`;
    if (milliseconds < 60_000) return `${(milliseconds / 1000).toFixed(1)} s`;
    return `${Math.round(milliseconds / 60_000)} min`;
  }

  protected identityKey(session: SessionSummary): string {
    return JSON.stringify([session.siteId, session.sensorId, session.eventId]);
  }

  protected fieldsOf(group: 'Ámbito' | 'Red'): FilterField[] {
    return this.availableFields().filter((field) => field.group === group);
  }

  private focus(id: string): void {
    this.host.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus();
  }
}
