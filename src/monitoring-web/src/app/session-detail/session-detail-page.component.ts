import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { SessionDetail, SessionDetailApi } from './session-detail-api';

type SessionDetailView =
  | { kind: 'empty' }
  | { kind: 'error' }
  | { kind: 'loading' }
  | { kind: 'success'; session: SessionDetail };

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-session-detail-page',
  styleUrl: './session-detail-page.component.css',
  templateUrl: './session-detail-page.component.html',
})
export class SessionDetailPage {
  private readonly api = inject(SessionDetailApi);
  private readonly retryTick = signal(0);
  private readonly eventId = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('eventId') ?? '')),
    { initialValue: inject(ActivatedRoute).snapshot.paramMap.get('eventId') ?? '' },
  );

  private readonly detail = rxResource({
    params: () => {
      const eventId = this.eventId();
      return eventId.length > 0 ? { eventId, retryTick: this.retryTick() } : undefined;
    },
    stream: ({ params }) => this.api.get(params.eventId),
  });

  protected readonly view = computed((): SessionDetailView => {
    const status = this.detail.status();
    if (status === 'loading' || status === 'reloading' || status === 'idle') {
      return { kind: 'loading' };
    }
    if (status === 'error') {
      return httpStatus(this.detail.error()) === 404 ? { kind: 'empty' } : { kind: 'error' };
    }
    const session = this.detail.value();
    if (session) {
      return { kind: 'success', session };
    }
    return { kind: 'loading' };
  });

  protected dataText(session: SessionDetail): string {
    return JSON.stringify(session.data);
  }

  protected retry(): void {
    this.retryTick.update((tick) => tick + 1);
  }

  protected retryFromKeyboard(event: Event): void {
    event.preventDefault();
    this.retry();
  }
}

function httpStatus(error: unknown): number {
  if (error instanceof HttpErrorResponse) {
    return error.status;
  }
  if (error instanceof Error && error.cause instanceof HttpErrorResponse) {
    return error.cause.status;
  }
  return 0;
}
