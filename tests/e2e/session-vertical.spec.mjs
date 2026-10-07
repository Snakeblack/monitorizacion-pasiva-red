import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { http, until } from '../../scripts/lab/compose.mjs';
import { ingestSyntheticSession } from '../stack/fixtures.mjs';

// The API runs without identity here; the OIDC overlay has its own spec (identity-keycloak.spec.mjs).
test.skip(process.env.E2E_IDENTITY === '1', 'The API is in OIDC mode.');

// ingestion inbox -> projector -> authority PostgreSQL -> outbox -> Debezium -> Kafka -> sink -> Elasticsearch -> API -> Angular list -> detail.
test('a committed session is listed and opens its five-field detail in the browser', async ({ page }) => {
  const eventId = `e2e-${randomUUID()}`;
  const sourceIp = `203.0.113.${100 + Math.floor(Math.random() * 100)}`;
  const startedAt = new Date(Date.now() - 5 * 60 * 1000);
  const searchDocumentId = await ingestSyntheticSession(eventId, {
    sourceIp, startedAt: startedAt.toISOString(), endedAt: new Date(startedAt.getTime() + 500).toISOString()
  });
  await until(() => http('elasticsearch:9200', `/sessions-v2-000001/_doc/${searchDocumentId}`), x => x.status === 200, 60);
  http('elasticsearch:9200', '/sessions-v2-000001/_refresh', 'POST');

  await page.goto('/sessions');
  await page.getByRole('button', { name: 'Últimas 24 h' }).click();
  await page.locator('#sourceIp').fill(sourceIp);
  await page.getByRole('button', { name: 'Buscar sesiones' }).click();

  const open = page.getByRole('link', { name: `Abrir detalle ${eventId}` });
  await expect(open).toBeVisible({ timeout: 30_000 });
  await open.click();

  await expect(page.getByRole('heading', { name: 'Detalle de sesión' })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('Sesión en ámbito');
  for (const label of ['eventId', 'siteId', 'sensorId', 'occurredAt', 'data'])
    await expect(page.locator('dt', { hasText: new RegExp(`^${label}$`) })).toBeVisible();
  await expect(page.locator('dd').first()).toHaveText(eventId);
});
