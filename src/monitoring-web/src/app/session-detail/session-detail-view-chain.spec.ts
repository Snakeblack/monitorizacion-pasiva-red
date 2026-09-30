import { provideHttpClient, withFetch } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { SESSION_DETAIL_API_ORIGIN } from './session-detail-api';
import { SessionDetailPage } from './session-detail-page.component';

const validData =
  '{"kind":"synthetic-session","version":1,"sourceIp":"192.0.2.1","destinationIp":"2001:db8::2","sourcePort":0,"destinationPort":65535,"protocol":"TCP","startedAt":"2026-09-29T12:00:00Z","endedAt":"2026-09-29T12:00:00.123Z"}';

type ViewChainChild = {
  stdin: { end(): void };
  stdout: { on(event: 'data', listener: (chunk: string) => void): void };
  stderr: { on(event: 'data', listener: (chunk: string) => void): void };
  on(event: 'exit', listener: (code: number | null) => void): void;
  kill(): boolean;
};

function repoRoot(): string {
  let dir = dirname(fileURLToPath(import.meta.url));
  for (;;) {
    if (existsSync(join(dir, 'Monitoring.slnx'))) {
      return dir;
    }
    const parent = dirname(dir);
    if (parent === dir) {
      throw new Error('Monitoring.slnx not found');
    }
    dir = parent;
  }
}

function waitForReady(child: ViewChainChild, timeoutMs: number): Promise<{ origin: string; eventId: string }> {
  return new Promise((resolve, reject) => {
    let stdout = '';
    let stderr = '';
    let settled = false;
    const timer = setTimeout(() => {
      finish(new Error(`VIEW_CHAIN_READY not received within ${timeoutMs}ms. stderr=${stderr} stdout=${stdout}`));
    }, timeoutMs);

    const finish = (error?: Error, value?: { origin: string; eventId: string }) => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      if (error) {
        reject(error);
      } else if (value) {
        resolve(value);
      }
    };

    child.stderr.on('data', (chunk) => {
      stderr += String(chunk);
    });
    child.stdout.on('data', (chunk) => {
      stdout += String(chunk);
      for (const line of stdout.split(/\r?\n/)) {
        const match = /^VIEW_CHAIN_READY (\S+) (\S+)$/.exec(line.trim());
        if (match) {
          finish(undefined, { origin: match[1], eventId: match[2] });
          return;
        }
      }
    });
    child.on('exit', (code) => {
      finish(new Error(`view-chain exited ${code} before VIEW_CHAIN_READY. stderr=${stderr} stdout=${stdout}`));
    });
  });
}

describe('SessionDetail fixture-to-view chain', () => {
  let child: ViewChainChild | undefined;
  let origin = '';
  let eventId = '';

  beforeAll(async () => {
    child = spawn(
      'dotnet',
      ['run', '--project', 'tests/Monitoring.Tests', '--no-build', '--', '--view-chain'],
      { cwd: repoRoot(), stdio: ['pipe', 'pipe', 'pipe'], env: process.env },
    ) as unknown as ViewChainChild;
    const ready = await waitForReady(child, 110_000);
    origin = ready.origin;
    eventId = ready.eventId;
  }, 120_000);

  afterAll(async () => {
    if (!child) {
      return;
    }
    child.stdin.end();
    await new Promise<void>((resolve) => {
      const timer = setTimeout(() => {
        child?.kill();
        resolve();
      }, 10_000);
      child?.on('exit', () => {
        clearTimeout(timer);
        resolve();
      });
    });
  }, 15_000);

  it('renders five persisted ValidData fields from the real GET', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'sessions/:eventId', component: SessionDetailPage }]),
        provideHttpClient(withFetch()),
        { provide: SESSION_DETAIL_API_ORIGIN, useValue: origin },
      ],
    });

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/sessions/${eventId}`, SessionDetailPage);

    await vi.waitFor(
      () => {
        expect(harness.routeNativeElement?.textContent ?? '').toContain('Sesión en ámbito');
      },
      { timeout: 10_000 },
    );

    const shown = harness.routeNativeElement?.textContent ?? '';
    expect(shown).toContain(eventId);
    expect(shown).toContain('site');
    expect(shown).toContain('sensor');
    expect(shown).toContain('2026-09-29T12:00:00.100Z');
    expect(shown).toContain('synthetic-session');
    expect(shown).toContain('192.0.2.1');
    expect(shown).toContain('TCP');
    expect(shown).toContain('65535');
    expect(shown).toContain(JSON.parse(validData).kind);
    expect(harness.routeNativeElement?.querySelectorAll('dt')).toHaveLength(5);
  });
});
