import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const project = process.env.MONITORING_COMPOSE_PROJECT ?? 'monitoring-foundation-test';
const root = fileURLToPath(new URL('../..', import.meta.url));
const args = ['compose', '--env-file', 'deploy/versions.env', '-p', project, '-f', 'compose.yaml'];
// Same daemon rule as scripts/lab/compose.mjs: Docker Desktop's, unless the older Ubuntu-WSL daemon is requested explicitly.
function compose(...command) {
  const viaWsl = process.platform === 'win32' && process.env.MONITORING_COMPOSE_VIA_WSL === '1';
  return spawnSync(viaWsl ? 'wsl.exe' : 'docker', viaWsl
    ? ['-d', 'Ubuntu', '--cd', root, 'env', '-u', 'DOCKER_CONTEXT', 'DOCKER_HOST=tcp://127.0.0.1:2375',
      'TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1', 'docker', ...args, ...command]
    : [...args, ...command], { cwd: root, encoding: 'utf8', timeout: 660000 });
}
test('core stack becomes healthy and retains an initialized canonical database on restart', () => {
  const started = compose('up', '--build', '--wait', '--wait-timeout', '300');
  assert.equal(started.status, 0, started.stderr);
  const query = compose('exec', '-T', 'postgres', 'psql', '-U', 'monitoring', '-d', 'monitoring', '-Atc',
    "SELECT count(*) FROM information_schema.tables WHERE table_schema='monitoring' AND table_name IN ('session_metadata','session_identity','projection_outbox')");
  assert.equal(query.status, 0, query.stderr);
  assert.equal(query.stdout.trim(), '3');
  const plugins = compose('exec', '-T', 'connect', 'bash', '-c',
    "exec 3<>/dev/tcp/localhost/8083; printf 'GET /connector-plugins HTTP/1.0\r\n\r\n' >&3; cat <&3");
  assert.equal(plugins.status, 0, plugins.stderr);
  assert.match(plugins.stdout, /io.debezium.connector.postgresql.PostgresConnector/);
  assert.match(plugins.stdout, /io.confluent.connect.elasticsearch.ElasticsearchSinkConnector/);
  assert.equal(compose('restart', 'postgres').status, 0);
  assert.equal(compose('up', '--wait', '--wait-timeout', '120').status, 0);
});
