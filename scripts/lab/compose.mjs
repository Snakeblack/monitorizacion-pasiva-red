import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// The compose files are addressed relative to the repository root, whatever directory the caller runs from.
const root = fileURLToPath(new URL('../..', import.meta.url));
export const project = process.env.MONITORING_COMPOSE_PROJECT ?? 'monitoring-foundation-test';
if (!/^monitoring-[a-z0-9-]+$/.test(project)) throw new Error('Use a task-owned monitoring-* project.');
export function compose(args, input, timeout = 120000) {
  const base = ['compose', '--env-file', 'deploy/versions.env', '-p', project, '-f', 'compose.yaml'];
  // On Windows the daemon is Docker Desktop's unless the older Ubuntu-WSL daemon is requested explicitly.
  const windows = process.platform === 'win32' && process.env.MONITORING_COMPOSE_VIA_WSL === '1';
  const result = spawnSync(windows ? 'wsl.exe' : 'docker', windows
    ? ['-d', 'Ubuntu', '--cd', root, '--exec', 'env', '-u', 'DOCKER_CONTEXT', 'DOCKER_HOST=tcp://127.0.0.1:2375', 'docker', ...base, ...args]
    : [...base, ...args], { cwd: root, encoding: 'utf8', input, timeout, maxBuffer: 8 * 1024 * 1024 });
  if (result.status !== 0) throw new Error(`Compose ${args[0]} failed: ${result.stderr ?? result.error}`);
  return result.stdout;
}
export function sql(statement) {
  return compose(['exec', '-T', 'postgres', 'psql', '-U', 'monitoring', '-d', 'monitoring', '-v', 'ON_ERROR_STOP=1', '-At'], statement).trim();
}
export function http(service, path, method = 'GET', body) {
  const args = ['exec', '-T', 'elasticsearch', 'curl', '-sS', '--max-time', '15', '-w', 'HTTP_STATUS_%{http_code}', '-X', method,
    '-H', 'Content-Type: application/json', `http://${service}${path}`];
  if (body !== undefined) args.push('--data-binary', '@-');
  const response = compose(args, body === undefined ? undefined : JSON.stringify(body));
  const offset = response.lastIndexOf('HTTP_STATUS_');
  const text = response.slice(0, offset);
  return { status: Number(response.slice(offset + 12)), body: text ? JSON.parse(text) : undefined };
}
export async function until(read, predicate, seconds = 60) {
  const deadline = Date.now() + seconds * 1000;
  let value;
  do {
    value = read();
    if (predicate(value)) return value;
    await new Promise(resolve => setTimeout(resolve, 1000));
  } while (Date.now() < deadline);
  throw new Error(`Condition not reached: ${JSON.stringify(value)}`);
}
