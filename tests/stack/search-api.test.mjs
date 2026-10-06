import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { http, sql, until } from '../../scripts/lab/compose.mjs';
import { commitFixture } from './fixtures.mjs';

// Real chain: authority PostgreSQL -> outbox -> Debezium -> Kafka -> sink -> Elasticsearch -> API (PIT, cursor, authority check).
// Requires the stack and `node scripts/lab/pipeline.mjs`; the API container is scoped to pipeline-site/pipeline-sensor.
const iso = date => date.toISOString();
const api = path => http('api:8080', path);
const searchDocument = searchDocumentId => http('elasticsearch:9200', `/sessions-v2-000001/_doc/${searchDocumentId}`);

test('the API lists indexed sessions in pages, hides a suppressed identity before the index catches up, and rejects reused cursors', async () => {
  // A source address unique to this run isolates the assertions from fixtures left by other stack tests.
  const sourceIp = `203.0.113.${100 + Math.floor(Math.random() * 100)}`;
  const base = Date.now() - 3600 * 1000;
  const events = [1, 2, 3].map(n => commitFixture(`api-${randomUUID()}`, {
    sourceIp, startedAt: iso(new Date(base - n * 1000)), endedAt: iso(new Date(base - n * 1000 + 500)), acceptedAt: iso(new Date(base))
  }));
  for (const event of events) await until(() => searchDocument(event.searchDocumentId), x => x.status === 200, 60);
  http('elasticsearch:9200', '/sessions-v2-000001/_refresh', 'POST');

  const window = `from=${encodeURIComponent(iso(new Date(Date.now() - 12 * 3600 * 1000)))}&to=${encodeURIComponent(iso(new Date()))}&sourceIp=${encodeURIComponent(sourceIp)}&pageSize=2`;
  const first = await until(() => api(`/api/v1/sessions?${window}`), x => x.status === 200 && x.body.items.length === 2, 30);
  assert.deepEqual(first.body.items.map(item => item.eventId), [events[0], events[1]].map(event => event.data.eventId));
  assert.ok(first.body.nextCursor, 'a third result remains, so a continuation is offered');
  assert.deepEqual(Object.keys(first.body).sort(), ['freshness', 'items', 'nextCursor']);
  assert.ok(['current', 'lagging', 'recovering'].includes(first.body.freshness.state));

  const second = api(`/api/v1/sessions?${window}&cursor=${encodeURIComponent(first.body.nextCursor)}`);
  assert.equal(second.status, 200);
  assert.deepEqual(second.body.items.map(item => item.eventId), [events[2].data.eventId]);
  assert.equal(second.body.nextCursor, null);

  // The cursor is bound to its filters: reuse with another filter is a validation failure, not a different page.
  assert.equal(api(`/api/v1/sessions?${window}&protocol=UDP&cursor=${encodeURIComponent(first.body.nextCursor)}`).status, 400);

  // The authority suppresses an identity; the index still holds its old document, yet the API must not serve it.
  sql(`BEGIN; SELECT pg_advisory_xact_lock_shared(7182041001);
    UPDATE monitoring.session_identity SET state='deleted',revision=2,deleted_at=now() WHERE search_document_id='${events[0].searchDocumentId}';
    COMMIT;`);
  const afterSuppression = api(`/api/v1/sessions?${window.replace('pageSize=2', 'pageSize=50')}`);
  assert.equal(afterSuppression.status, 200);
  assert.deepEqual(afterSuppression.body.items.map(item => item.eventId), [events[1], events[2]].map(event => event.data.eventId));
});

test('invalid and out-of-scope requests are rejected before any search', () => {
  const now = new Date();
  const window = `from=${encodeURIComponent(iso(new Date(now - 3600 * 1000)))}&to=${encodeURIComponent(iso(now))}`;
  assert.equal(api('/api/v1/sessions').status, 400);
  assert.equal(api(`/api/v1/sessions?${window}&pageSize=101`).status, 400);
  assert.equal(api(`/api/v1/sessions?${window}&siteId=another-site`).status, 403);
});
