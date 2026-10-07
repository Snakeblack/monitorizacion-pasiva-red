import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { combineLatest, map, Subject, takeUntil, tap } from 'rxjs';
import { Icon } from '../ui/icon.component';
import { SessionDetail, SessionDetailApi } from './session-detail-api';
import { sessionErrorMessage, sessionHttpStatus } from '../sessions/session-http-error';

type SessionDetailView =
  | { kind: 'empty' }
  | { kind: 'error'; message: string }
  | { kind: 'loading' }
  | { kind: 'success'; session: SessionDetail };

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RouterLink],
  selector: 'app-session-detail-page',
  styleUrl: './session-detail-page.component.css',
  templateUrl: './session-detail-page.component.html',
})
export class SessionDetailPage {
  private readonly api = inject(SessionDetailApi);
  private readonly retryTick = signal(0);
  private readonly cancelDetail = new Subject<void>();
  private readonly route = inject(ActivatedRoute);
  private readonly identity = toSignal(
    combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(
      map(([params, query]) => ({
        eventId: params.get('eventId') ?? '',
        siteId: query.get('siteId') ?? '',
        sensorId: query.get('sensorId') ?? '',
      })),
      tap(() => this.cancelDetail.next()),
    ),
    {
      initialValue: {
        eventId: this.route.snapshot.paramMap.get('eventId') ?? '',
        siteId: this.route.snapshot.queryParamMap.get('siteId') ?? '',
        sensorId: this.route.snapshot.queryParamMap.get('sensorId') ?? '',
      },
    },
  );
  private readonly identityError = computed(() => {
    const identity = this.identity();
    if (!identity.eventId) return 'Falta la identidad de la sesión.';
    if (!!identity.siteId !== !!identity.sensorId)
      return 'Indica sede y sonda para una identidad completa.';
    return '';
  });

  private readonly detail = rxResource({
    params: () => {
      return this.identityError() ? undefined : { ...this.identity(), retryTick: this.retryTick() };
    },
    stream: ({ params }) =>
      this.api
        .get(
          params.eventId,
          params.siteId ? { siteId: params.siteId, sensorId: params.sensorId } : undefined,
        )
        .pipe(takeUntil(this.cancelDetail)),
  });

  protected readonly view = computed((): SessionDetailView => {
    if (this.identityError()) return { kind: 'error', message: this.identityError() };
    const status = this.detail.status();
    if (status === 'loading' || status === 'reloading' || status === 'idle') {
      return { kind: 'loading' };
    }
    if (status === 'error') {
      const status = sessionHttpStatus(this.detail.error());
      return status === 404
        ? { kind: 'empty' }
        : { kind: 'error', message: sessionErrorMessage(status) };
    }
    const session = this.detail.value();
    if (session) {
      return { kind: 'success', session };
    }
    return { kind: 'loading' };
  });

  protected readonly copied = signal(false);

  protected dataText(session: SessionDetail): string {
    return JSON.stringify(session.data, null, 2);
  }

  /** The protocol the sensor recorded, when the payload carries one; the payload itself is shown untouched below. */
  protected protocolOf(session: SessionDetail): string {
    const data: unknown = session.data;
    const protocol = typeof data === 'object' && data !== null ? (data as Record<string, unknown>)['protocol'] : undefined;
    return typeof protocol === 'string' ? protocol : '';
  }

  protected copy(value: string): void {
    void navigator.clipboard?.writeText(value).then(
      () => {
        this.copied.set(true);
        setTimeout(() => this.copied.set(false), 1800);
      },
      () => undefined,
    );
  }

  protected retry(): void {
    this.retryTick.update((tick) => tick + 1);
  }

  protected retryFromKeyboard(event: Event): void {
    event.preventDefault();
    this.retry();
  }
}
