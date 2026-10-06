import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// Pure sink-guard checks: no containers. The pinned Kafka API jars come from scripts/lab/build-transform.ps1.
const libs = 'deploy/connect/transforms/build/libs';
const classpath = existsSync(libs) ? `${libs}/*` : null;

test('session contract guard enforces schema 2 and re-keys to the compact search identity', { skip: classpath ? false : 'run scripts/lab/build-transform.ps1 first' }, () => {
  const out = mkdtempSync(join(tmpdir(), 'session-contract-'));
  try {
    execFileSync('javac', ['--release', '21', '-cp', classpath, '-d', out,
      'deploy/connect/transforms/src/SessionContract.java', 'deploy/connect/transforms/test/SessionContractTest.java'], { stdio: 'pipe' });
    const output = execFileSync('java', ['-cp', `${out}:${classpath}`, 'monitoring.SessionContractTest'], { encoding: 'utf8' });
    assert.match(output, /\d+ checks passed/);
  } finally { rmSync(out, { recursive: true, force: true }); }
});
