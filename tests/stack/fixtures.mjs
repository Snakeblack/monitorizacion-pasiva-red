import { randomUUID } from 'node:crypto';
import { sql } from '../../scripts/lab/compose.mjs';

export const key = (site, sensor, event) => [site,sensor,event].map(x=>Buffer.from(x).toString('base64url')).join('.');

// Commits an identity and its outbox record in one authority transaction, as the projector does, bypassing ingestion.
export function commitFixture(event, fields = {}, site = 'pipeline-site', sensor = 'pipeline-sensor') {
  const documentKey = key(site,sensor,event);
  const searchDocumentId = randomUUID();
  const data = { schemaVersion:2, operation:'upsert', documentKey, searchDocumentId, revision:1, siteId:site, sensorId:sensor,eventId:event,
    startedAt:'2026-10-05T10:00:00.100Z',endedAt:'2026-10-05T10:00:01.000Z',sourceIp:'2001:db8::1',destinationIp:'192.0.2.2',
    sourcePort:1234,destinationPort:443,protocol:'TCP',vlanId:null,provenance:'synthetic',inferred:null,partial:null,closeReason:null,packetCount:null,byteCount:null,acceptedAt:'2026-10-05T10:00:02.000Z', ...fields };
  sql(`BEGIN;
    INSERT INTO monitoring.session_identity(site_id,sensor_id,event_id,document_key,search_document_id,revision,state) VALUES ('${site}','${sensor}','${event}','${documentKey}','${searchDocumentId}',1,'active');
    INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload)
    VALUES ('${randomUUID()}','${documentKey}','sessions','monitoring.sessions.v2',1,2,'${JSON.stringify(data)}');
    COMMIT;`);
  return { documentKey, searchDocumentId, data };
}

