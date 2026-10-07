import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { compose, http, sql, until } from '../../scripts/lab/compose.mjs';
import { commitFixture } from './fixtures.mjs';

// The index _id is the compact authority-assigned search identity, never the (up to 515-byte) document key.
const document = searchDocumentId => http('elasticsearch:9200',`/sessions-v2-000001/_doc/${searchDocumentId}`);
const offsets = topic => compose(['exec','-T','kafka','/opt/kafka/bin/kafka-get-offsets.sh','--bootstrap-server','kafka:9092','--topic',topic]).trim();
function replay(record, header = record.data.revision) {
  compose(['exec','-T','kafka','/opt/kafka/bin/kafka-console-producer.sh','--bootstrap-server','kafka:9092','--topic','monitoring.sessions.v2',
    '--property','parse.key=true','--property','parse.headers=true','--producer-property','enable.idempotence=true'],
    `revision:${header}\t${record.documentKey}\t${JSON.stringify(record.data)}\n`);
}
test('committed authority outbox reaches the real sink with complete identity and authority revision', async () => {
  const event = `commit-${randomUUID()}`;
  const {documentKey, searchDocumentId, data} = commitFixture(event);
  const result = await until(()=>document(searchDocumentId), x=>x.status===200, 12);
  assert.equal(result.body._version,1);
  assert.deepEqual(result.body._source,data);
  http('elasticsearch:9200','/sessions-v2-000001/_refresh','POST');
  const search = http('elasticsearch:9200','/sessions-read/_search','POST',{query:{term:{documentKey}}});
  assert.equal(search.status,200);
  assert.equal(search.body.hits.total.value,1);
  assert.equal(search.body.hits.hits[0]._id,searchDocumentId);
});

test('a maximum-length identity above the 512-byte _id limit is indexed under its compact search identity', async () => {
  const [site,sensor]=['s','n'].map(letter=>letter.repeat(128));
  // Unique per run so the test can be repeated on a stack that already holds an earlier identity; the length stays at the limit.
  const event=(randomUUID().replaceAll('-','')+'e'.repeat(128)).slice(0,128);
  const {documentKey, searchDocumentId, data} = commitFixture(event,{},site,sensor);
  assert.equal(Buffer.byteLength(documentKey),515);
  const result = await until(()=>document(searchDocumentId), x=>x.status===200, 15);
  assert.equal(Buffer.byteLength(searchDocumentId),36);
  assert.equal(result.body._source.documentKey,documentKey);
  assert.deepEqual(result.body._source,data);
  http('elasticsearch:9200','/sessions-v2-000001/_refresh','POST');
  const search = http('elasticsearch:9200','/sessions-read/_search','POST',{query:{term:{documentKey}}});
  assert.equal(search.body.hits.total.value,1);
  assert.equal(search.body.hits.hits[0]._id,searchDocumentId);
});

test('unsupported contract is durably isolated while a later valid identity continues', async () => {
  const before=offsets('monitoring.sessions.dlq');
  const invalid=commitFixture(`invalid-${randomUUID()}`,{schemaVersion:999});
  const valid=commitFixture(`after-invalid-${randomUUID()}`);
  await until(()=>document(valid.searchDocumentId),x=>x.status===200,15);
  assert.equal(document(invalid.searchDocumentId).status,404);
  await until(()=>offsets('monitoring.sessions.dlq'),x=>x!==before,15);
  const offset=Number(before.split(':').at(-1));
  const dlq=compose(['exec','-T','kafka','/opt/kafka/bin/kafka-console-consumer.sh','--bootstrap-server','kafka:9092','--topic','monitoring.sessions.dlq',
    '--partition','0','--offset',String(offset),'--max-messages','1','--timeout-ms','10000','--formatter-property','print.headers=true']);
  assert.match(dlq,/contract-version/);
  assert.ok(dlq.includes(invalid.data.eventId));
  assert.equal(http('connect:8083','/connectors/monitoring-sink/status').body.tasks[0].state,'RUNNING');
});

test('minimal permanent delete document resists old upserts at new Kafka offsets and restart', async () => {
  const record=commitFixture(`delete-${randomUUID()}`);
  await until(()=>document(record.searchDocumentId),x=>x.status===200,15);
  const barrier={schemaVersion:2,operation:'delete',documentKey:record.documentKey,searchDocumentId:record.searchDocumentId,revision:2,siteId:'pipeline-site',sensorId:'pipeline-sensor',eventId:record.data.eventId};
  sql(`BEGIN; SELECT pg_advisory_xact_lock_shared(7182041001);
    UPDATE monitoring.session_identity SET state='deleted',revision=2,deleted_at=now() WHERE document_key='${record.documentKey}';
    INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload)
    VALUES ('${randomUUID()}','${record.documentKey}','sessions','monitoring.sessions.v2',2,2,'${JSON.stringify(barrier)}'); COMMIT;`);
  await until(()=>document(record.searchDocumentId),x=>x.status===200&&x.body._version===2,15);
  const before=offsets('monitoring.sessions.v2');
  replay(record);
  const sentinel=commitFixture(`after-replay-${randomUUID()}`);
  await until(()=>document(sentinel.searchDocumentId),x=>x.status===200,15);
  assert.notEqual(offsets('monitoring.sessions.v2'),before);
  assert.deepEqual(document(record.searchDocumentId).body._source,barrier);
  assert.equal(document(record.searchDocumentId).body._version,2);
  compose(['restart','connect']);
  compose(['up','--wait','--wait-timeout','120','connect']);
  await until(()=>http('connect:8083','/connectors/monitoring-sink/status'),x=>x.body?.tasks?.[0]?.state==='RUNNING',45);
  replay(record);
  const resumed=commitFixture(`after-restart-${randomUUID()}`);
  await until(()=>document(resumed.searchDocumentId),x=>x.status===200,30);
  assert.deepEqual(document(record.searchDocumentId).body._source,barrier);
});

test('real search outage preserves confirmed outbox and retries valid publication after recovery', async () => {
  compose(['stop','elasticsearch']);
  try {
    const before=offsets('monitoring.sessions.v2');
    const record=commitFixture(`outage-${randomUUID()}`);
    await until(()=>offsets('monitoring.sessions.v2'),x=>x!==before,20);
    assert.equal(sql(`SELECT count(*) FROM monitoring.projection_outbox WHERE aggregateid='${record.documentKey}'`),'1');
    compose(['up','--wait','--wait-timeout','120','elasticsearch']);
    const result=await until(()=>document(record.searchDocumentId),x=>x.status===200,60);
    assert.equal(result.body._source.eventId,record.data.eventId);
    assert.equal(result.body._version,1);
  } finally { compose(['up','--wait','--wait-timeout','120','elasticsearch']); }
});
