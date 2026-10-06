import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { compose, http, sql, until } from '../../scripts/lab/compose.mjs';

const key = (site, sensor, event) => [site,sensor,event].map(x=>Buffer.from(x).toString('base64url')).join('.');
const document = documentKey => http('elasticsearch:9200',`/sessions-v1-000001/_doc/${documentKey}`);
const offsets = topic => compose(['exec','-T','kafka','/opt/kafka/bin/kafka-get-offsets.sh','--bootstrap-server','kafka:9092','--topic',topic]).trim();
function replay(record, header = record.data.revision) {
  compose(['exec','-T','kafka','/opt/kafka/bin/kafka-console-producer.sh','--bootstrap-server','kafka:9092','--topic','monitoring.sessions.v1',
    '--property','parse.key=true','--property','parse.headers=true','--producer-property','enable.idempotence=true'],
    `revision:${header}\t${record.documentKey}\t${JSON.stringify(record.data)}\n`);
}
export function commitFixture(event, fields = {}) {
  const documentKey = key('pipeline-site','pipeline-sensor',event);
  const data = { schemaVersion:1, operation:'upsert', documentKey, revision:1, siteId:'pipeline-site', sensorId:'pipeline-sensor',eventId:event,
    startedAt:'2026-10-05T10:00:00.100Z',endedAt:'2026-10-05T10:00:01.000Z',sourceIp:'2001:db8::1',destinationIp:'192.0.2.2',
    sourcePort:1234,destinationPort:443,protocol:'TCP',vlanId:null,provenance:'synthetic',inferred:null,partial:null,closeReason:null,packetCount:null,byteCount:null,acceptedAt:'2026-10-05T10:00:02.000Z', ...fields };
  sql(`BEGIN;
    INSERT INTO monitoring.session_identity(site_id,sensor_id,event_id,document_key,revision,state) VALUES ('pipeline-site','pipeline-sensor','${event}','${documentKey}',1,'active');
    INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload)
    VALUES ('${randomUUID()}','${documentKey}','sessions','monitoring.sessions.v1',1,1,'${JSON.stringify(data)}');
    COMMIT;`);
  return { documentKey, data };
}

test('committed authority outbox reaches the real sink with complete identity and authority revision', async () => {
  const event = `commit-${randomUUID()}`;
  const {documentKey, data} = commitFixture(event);
  const result = await until(()=>http('elasticsearch:9200',`/sessions-v1-000001/_doc/${documentKey}`), x=>x.status===200, 12);
  assert.equal(result.body._version,1);
  assert.deepEqual(result.body._source,data);
  http('elasticsearch:9200','/sessions-v1-000001/_refresh','POST');
  const search = http('elasticsearch:9200','/sessions-read/_search','POST',{query:{term:{documentKey}}});
  assert.equal(search.status,200);
  assert.equal(search.body.hits.total.value,1);
});

test('unsupported contract is durably isolated while a later valid identity continues', async () => {
  const before=offsets('monitoring.sessions.dlq');
  const invalid=commitFixture(`invalid-${randomUUID()}`,{schemaVersion:999});
  const valid=commitFixture(`after-invalid-${randomUUID()}`);
  await until(()=>http('elasticsearch:9200',`/sessions-v1-000001/_doc/${valid.documentKey}`),x=>x.status===200,15);
  assert.equal(http('elasticsearch:9200',`/sessions-v1-000001/_doc/${invalid.documentKey}`).status,404);
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
  await until(()=>document(record.documentKey),x=>x.status===200,15);
  const barrier={schemaVersion:1,operation:'delete',documentKey:record.documentKey,revision:2,siteId:'pipeline-site',sensorId:'pipeline-sensor',eventId:record.data.eventId};
  sql(`BEGIN; SELECT pg_advisory_xact_lock_shared(7182041001);
    UPDATE monitoring.session_identity SET state='deleted',revision=2,deleted_at=now() WHERE document_key='${record.documentKey}';
    INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload)
    VALUES ('${randomUUID()}','${record.documentKey}','sessions','monitoring.sessions.v1',2,1,'${JSON.stringify(barrier)}'); COMMIT;`);
  await until(()=>document(record.documentKey),x=>x.status===200&&x.body._version===2,15);
  const before=offsets('monitoring.sessions.v1');
  replay(record);
  const sentinel=commitFixture(`after-replay-${randomUUID()}`);
  await until(()=>document(sentinel.documentKey),x=>x.status===200,15);
  assert.notEqual(offsets('monitoring.sessions.v1'),before);
  assert.deepEqual(document(record.documentKey).body._source,barrier);
  assert.equal(document(record.documentKey).body._version,2);
  compose(['restart','connect']);
  compose(['up','--wait','--wait-timeout','120','connect']);
  await until(()=>http('connect:8083','/connectors/monitoring-sink/status'),x=>x.body?.tasks?.[0]?.state==='RUNNING',45);
  replay(record);
  const resumed=commitFixture(`after-restart-${randomUUID()}`);
  await until(()=>document(resumed.documentKey),x=>x.status===200,30);
  assert.deepEqual(document(record.documentKey).body._source,barrier);
});

test('real search outage preserves confirmed outbox and retries valid publication after recovery', async () => {
  compose(['stop','elasticsearch']);
  try {
    const before=offsets('monitoring.sessions.v1');
    const record=commitFixture(`outage-${randomUUID()}`);
    await until(()=>offsets('monitoring.sessions.v1'),x=>x!==before,20);
    assert.equal(sql(`SELECT count(*) FROM monitoring.projection_outbox WHERE aggregateid='${record.documentKey}'`),'1');
    compose(['up','--wait','--wait-timeout','120','elasticsearch']);
    const result=await until(()=>document(record.documentKey),x=>x.status===200,60);
    assert.equal(result.body._source.eventId,record.data.eventId);
    assert.equal(result.body._version,1);
  } finally { compose(['up','--wait','--wait-timeout','120','elasticsearch']); }
});
