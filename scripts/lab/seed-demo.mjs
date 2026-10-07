import { sql, until, project } from './compose.mjs';

// Seeds believable demo data through the real ingestion inbox (the same table the ingestion endpoint writes), so everything downstream is
// genuine: the projection worker writes the canonical sessions, Debezium carries them through Kafka to Elasticsearch, and the observations
// become inventory candidates. Safe to run again: rows that already exist are skipped.
//
//   MONITORING_COMPOSE_PROJECT=monitoring-local node scripts/lab/seed-demo.mjs
//
// Two sites, like the laboratory users: analista sees only site A, auditor only site B, admin-inventario sees both.
const SITES = [
  { key: 'a', site: 'pipeline-site', sensor: 'pipeline-sensor', label: 'sede A', sources: ['192.0.2.', '198.51.100.'], subnet: '10.10.1.' },
  { key: 'b', site: 'lab-site-b', sensor: 'lab-sensor-b', label: 'sede B', sources: ['203.0.113.', '198.18.7.'], subnet: '10.20.1.' },
];
const SESSIONS_PER_SITE = 40;
const DESTINATIONS = [['443', 'TCP'], ['443', 'TCP'], ['53', 'UDP'], ['22', 'TCP'], ['8080', 'TCP'], ['123', 'UDP'], ['3389', 'TCP'], ['5353', 'UDP']];
const DEVICES = [
  ['3c:22:fb:10:aa:01', 10], ['3c:22:fb:10:aa:02', 10], ['f0:9f:c2:5b:00:11', 20], ['b8:27:eb:6c:3d:42', null], ['00:1b:63:84:45:e6', 20], ['dc:a6:32:0f:19:7a', null],
];

// A small deterministic generator, so two runs produce the same event ids and the same shapes.
let state = 20261007;
const next = () => (state = (state * 1664525 + 1013904223) % 4294967296) / 4294967296;
const pick = list => list[Math.floor(next() * list.length)];
const quote = text => `'${String(text).replaceAll("'", "''")}'`;

const now = Date.now();
const rows = [];
for (const place of SITES) {
  for (let i = 0; i < SESSIONS_PER_SITE; i++) {
    // Spread over the last 20 hours, denser recently.
    const ageMinutes = Math.floor((next() ** 2) * 20 * 60) + 1;
    const startedAt = new Date(now - ageMinutes * 60_000 - Math.floor(next() * 50_000));
    const [port, protocol] = pick(DESTINATIONS);
    const durationMs = protocol === 'UDP' ? 40 + Math.floor(next() * 400) : 200 + Math.floor(next() * 90_000);
    const v6 = next() < 0.12;
    const data = {
      kind: 'synthetic-session', version: 1,
      sourceIp: v6 ? `2001:db8:${place.key}::${(10 + Math.floor(next() * 200)).toString(16)}` : `${pick(place.sources)}${10 + Math.floor(next() * 200)}`,
      destinationIp: v6 ? '2001:db8:ffff::53' : `${place.subnet}${1 + Math.floor(next() * 40)}`,
      sourcePort: 32768 + Math.floor(next() * 28000), destinationPort: Number(port), protocol,
      startedAt: startedAt.toISOString(), endedAt: new Date(startedAt.getTime() + durationMs).toISOString(),
    };
    rows.push({ place, event: `demo-${place.key}-${String(i + 1).padStart(3, '0')}`, at: data.startedAt, data });
  }
  DEVICES.forEach(([mac, vlan], index) => {
    // A device is seen a few times, from addresses in this site's subnet; some devices exist only in one site.
    if (place.key === 'b' && index % 3 === 2) return;
    for (let seen = 0; seen < 3; seen++) {
      const at = new Date(now - (index * 37 + seen * 95 + 5) * 60_000).toISOString().replace(/\.\d+Z$/, 'Z');
      rows.push({
        place, event: `demo-obs-${place.key}-${index + 1}-${seen + 1}`, at,
        data: { kind: 'device-observation', version: 1, observedAt: at, ip: `${place.subnet}${100 + index}`, mac: place.key === 'b' ? mac.replace(/^(..):/, (_, first) => `${(parseInt(first, 16) ^ 0x02).toString(16).padStart(2, '0')}:`) : mac, vlanId: vlan },
      });
    }
  });
}

for (const place of SITES)
  sql(`INSERT INTO monitoring.ingestion_origin(site_id,sensor_id) VALUES (${quote(place.site)},${quote(place.sensor)}) ON CONFLICT DO NOTHING;`);

const values = rows.map(({ place, event, at, data }) =>
  `(${quote(place.site)},${quote(place.sensor)},${quote(event)},'demo-seed',1,${quote(at)},${quote(at)},${quote(JSON.stringify(data))}::jsonb,now())`);
const inserted = sql(`WITH incoming(site_id,sensor_id,event_id,batch_id,schema_version,occurred_at,occurred_at_text,data,accepted_at) AS (VALUES ${values.join(',')}),
  fresh AS (SELECT i.* FROM incoming i WHERE NOT EXISTS (SELECT 1 FROM monitoring.ingestion_inbox x WHERE x.site_id=i.site_id AND x.sensor_id=i.sensor_id AND x.event_id=i.event_id)),
  done AS (INSERT INTO monitoring.ingestion_inbox(site_id,sensor_id,event_id,batch_id,schema_version,occurred_at,occurred_at_text,data,accepted_at)
    SELECT site_id,sensor_id,event_id,batch_id,schema_version,occurred_at::timestamptz,occurred_at_text,data,accepted_at FROM fresh RETURNING 1)
  SELECT count(*) FROM done;`);
console.log(`Project ${project}: ${inserted} new events accepted, ${rows.length - Number(inserted)} already present.`);

const sessions = rows.filter(row => row.data.kind === 'synthetic-session').length;
await until(() => Number(sql(`SELECT count(*) FROM monitoring.session_identity WHERE event_id LIKE 'demo-%'`)), count => count >= sessions, 120);
console.log(`Projected: ${sessions} demo sessions are in the authority and on their way to the search index (Debezium -> Kafka -> Elasticsearch).`);
const candidates = sql(`SELECT count(*) FROM monitoring.device_candidate`).trim();
console.log(`Inventory candidates now: ${candidates}.`);
