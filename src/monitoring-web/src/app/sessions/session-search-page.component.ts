import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormField, form, max, min, required, validate } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { recentSessionFilters, SessionFilters, sessionFilterError } from './session-query';
import { SessionSearchApi, SessionSummary } from './session-search-api';
import { sessionErrorMessage, sessionHttpStatus } from './session-http-error';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormField, RouterLink],
  selector: 'app-session-search-page',
  templateUrl: './session-search-page.component.html',
  styleUrl: './session-search-page.component.css',
})
export class SessionSearchPage {
  private readonly api = inject(SessionSearchApi);
  private readonly cancelSearch = new Subject<void>();
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
  protected readonly attempted = signal(false);
  protected readonly validation = computed(() =>
    this.attempted() ? sessionFilterError(this.model(), Date.now()) : '',
  );
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
      ? sessionErrorMessage(sessionHttpStatus(this.results.error()))
      : '',
  );
  protected readonly expired = computed(
    () => this.results.status() === 'error' && sessionHttpStatus(this.results.error()) === 410,
  );
  protected readonly freshnessText = computed(() => {
    const freshness = this.page()?.freshness;
    if (!freshness) return '';
    const state = {
      current: 'Índice al día',
      lagging: 'Índice retrasado',
      recovering: 'Índice en recuperación',
    }[freshness.state];
    const lag =
      freshness.lagSeconds == null
        ? 'Retraso desconocido'
        : `Retraso conocido: ${freshness.lagSeconds} s`;
    return `${state} · ${lag} · Medido: ${freshness.measuredAt}`;
  });

  protected filtersChanged(): void {
    // Angular 22 keeps an in-flight stream when params become undefined. Cancel HTTP explicitly.
    this.request.set(undefined);
    this.cancelSearch.next();
    this.attempted.set(false);
  }

  protected search(event?: Event): void {
    event?.preventDefault();
    this.attempted.set(true);
    if (sessionFilterError(this.model(), Date.now())) {
      this.request.set(undefined);
      return;
    }
    this.request.set({ filters: { ...this.model() }, cursor: '', attempt: ++this.attempt });
  }

  protected recent(): void {
    this.model.set(recentSessionFilters(Date.now()));
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

  protected identityKey(session: SessionSummary): string {
    return JSON.stringify([session.siteId, session.sensorId, session.eventId]);
  }
}
