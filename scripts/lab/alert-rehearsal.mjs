import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Rehearses real failures against the running lab stack (ADR-020): each one is provoked, the alert must fire in the lab Prometheus,
// the fault is undone and the alert must resolve. Requires the stack, `node scripts/lab/pipeline.mjs`, the `observability` and
// `observability-lab` profiles, the API published on 127.0.0.1:5080 (deploy/compose.e2e.yaml) and TELEMETRY_OTLP_ENDPOINT set on the API.
// Alerts that need 5-15 minutes to fire are covered by `promtool test rules` (deploy/observability/alerts.test.yml).
const project = process.env.MONITORING_COMPOSE_PROJECT ?? 'monitoring-local';
const root = fileURLToPath(new URL('../..', import.meta.url));
const base = ['compose', '--env-file', 'deploy/versions.env', '-p', project, '-f', 'compose.yaml', '-f', 'deploy/compose.e2e.yaml',
  '--profile', 'observability', '--profile', 'observability-lab'];

function compose(args, input) {
  const result = spawnSync('docker', [...base, ...args], { cwd: root, encoding: 'utf8', input, timeout: 180000 });
  if (result.status !== 0) throw new Error(`compose ${args.join(' ')} failed: ${result.stderr}`);
  return result.stdout;
}
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const firing = () => {
  const text = compose(['exec', '-T', 'prometheus', 'wget', '-qO-', 'http://localhost:9090/api/v1/query?query=ALERTS%7Balertstate%3D%22firing%22%7D']);
  return new Set(JSON.parse(text).data.result.map(series => series.metric.alertname));
};
async function waitFor(name, wantFiring, seconds) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (firing().has(name) === wantFiring) return Math.round(seconds - (deadline - Date.now()) / 1000);
    await sleep(5000);
  }
  throw new Error(`${name} did not ${wantFiring ? 'fire' : 'resolve'} within ${seconds}s (firing: ${[...firing()].join(', ') || 'none'})`);
}
const connectCall = (method, path) => compose(['exec', '-T', 'elasticsearch', 'curl', '-sS', '-o', '/dev/null', '-w', '%{http_code}', '-X', method, `http://connect:8083${path}`]);
const results = [];
async function scenario(title, run) {
  const started = Date.now();
  process.stdout.write(`== ${title}\n`);
  await run();
  results.push([title, Math.round((Date.now() - started) / 1000)]);
}

await scenario('a record the sink cannot apply reaches the dead-letter queue -> SinkDeadLetterGrowing', async () => {
  compose(['exec', '-T', 'kafka', '/opt/kafka/bin/kafka-console-producer.sh', '--bootstrap-server', 'localhost:9092', '--topic', 'monitoring.sessions.v2',
    '--property', 'parse.key=true', '--property', 'key.separator=#'], 'rehearsal-key#{"not":"a session"}\n');
  console.log(`   fired after ${await waitFor('SinkDeadLetterGrowing', true, 150)}s (it stays firing for the 15 min window of increase())`);
});

await scenario('the sink task is paused -> ConnectTaskNotRunning, then resumed', async () => {
  if (connectCall('PUT', '/connectors/monitoring-sink/pause') !== '202') throw new Error('pause was not accepted');
  console.log(`   fired after ${await waitFor('ConnectTaskNotRunning', true, 240)}s`);
  if (connectCall('PUT', '/connectors/monitoring-sink/resume') !== '202') throw new Error('resume was not accepted');
  console.log(`   resolved after ${await waitFor('ConnectTaskNotRunning', false, 150)}s`);
});

await scenario('elasticsearch stops -> SearchEngineUnreachable, the API stays healthy, then it recovers', async () => {
  compose(['stop', 'elasticsearch']);
  try {
    console.log(`   fired after ${await waitFor('SearchEngineUnreachable', true, 300)}s`);
    const live = await fetch('http://127.0.0.1:5080/health/live');
    if (live.status !== 200) throw new Error(`the API is not live during the outage: ${live.status}`);
    const now = new Date();
    const search = await fetch(`http://127.0.0.1:5080/api/v1/sessions?from=${new Date(now - 3600e3).toISOString()}&to=${new Date(now - 5e3).toISOString()}`);
    console.log(`   API during the outage: /health/live 200, search ${search.status}`);
  } finally {
    compose(['up', '-d', '--wait', '--wait-timeout', '180', 'elasticsearch']);
  }
  console.log(`   resolved after ${await waitFor('SearchEngineUnreachable', false, 240)}s`);
});

await scenario('the metrics destination stops -> the API is unaffected (ADR-021)', async () => {
  compose(['stop', 'prometheus']);
  try {
    await sleep(40000);
    const live = await fetch('http://127.0.0.1:5080/health/live');
    if (live.status !== 200) throw new Error(`the API stopped answering while its metrics destination was down: ${live.status}`);
    console.log('   the API answered /health/live while the destination was down');
  } finally {
    compose(['up', '-d', '--wait', '--wait-timeout', '120', 'prometheus']);
  }
});

console.log('\nREHEARSAL OK');
for (const [title, seconds] of results) console.log(` - ${seconds}s  ${title}`);
