#!/usr/bin/env bash
# Point-in-time recovery rehearsal against a throw-away PostgreSQL cluster built from the local server binaries.
#
# It proves the recovery chain the runbook (docs/runbooks/continuity.md) relies on, on a single node:
#   base backup + continuous WAL archiving -> abrupt loss of the primary and a destructive mistake -> recovery to a named restore
#   point -> `--restore-finalize` re-applies retention -> migrations are a no-op -> the finalizer is idempotent.
# It does NOT prove: streaming standby / automatic failover, Kafka/Connect/Elasticsearch recovery, off-host backup storage, or any
# timing (RPO/RTO) on production hardware.
#
# Usage: scripts/lab/pitr-rehearsal.sh [path/to/Monitoring.Host.dll]
# Needs: initdb, pg_ctl, pg_basebackup, psql (set PG_BIN to their directory), dotnet, and either a non-root user or `runuser`.
set -euo pipefail

HOST_DLL=${1:-src/Monitoring.Host/bin/Debug/net10.0/Monitoring.Host.dll}
PG_BIN=${PG_BIN:-$(ls -d /usr/lib/postgresql/*/bin 2>/dev/null | sort -V | tail -1)}
PORT_PRIMARY=${PORT_PRIMARY:-55432}
PORT_RESTORED=${PORT_RESTORED:-55433}
[ -x "$PG_BIN/initdb" ] || { echo "PG_BIN does not contain initdb" >&2; exit 1; }
[ -f "$HOST_DLL" ] || { echo "Host assembly not found: $HOST_DLL (build it first)" >&2; exit 1; }
HOST_DLL=$(readlink -f "$HOST_DLL")

WORK=$(mktemp -d)
ARCHIVE=$WORK/wal-archive
if [ "$(id -u)" = 0 ]; then AS_PG=(runuser -u postgres --); chown postgres "$WORK"; else AS_PG=(); fi
on_error() { echo "--- last lines of the server logs ---" >&2; tail -n 15 "$WORK"/*.log >&2 2>/dev/null || true; }
trap on_error ERR
trap 'for d in primary restored; do "${AS_PG[@]}" "$PG_BIN/pg_ctl" -D "$WORK/$d" -m immediate stop >/dev/null 2>&1 || true; done; rm -rf "$WORK"' EXIT

pg() { "${AS_PG[@]}" "$@"; }
sql() { local port=$1; shift; "$PG_BIN/psql" -h 127.0.0.1 -p "$port" -U postgres -d monitoring -v ON_ERROR_STOP=1 -qAt -c "$*"; }
conn() { echo "Host=127.0.0.1;Port=$1;Database=monitoring;Username=postgres;Pooling=false"; }
host() { local port=$1; shift; ConnectionStrings__Monitoring=$(conn "$port") "$@"; }
dotnet_host() { local port=$1; shift; ConnectionStrings__Monitoring=$(conn "$port") dotnet "$HOST_DLL" "$@"; }
expect() { [ "$2" = "$3" ] || { echo "FAIL: $1 (expected $3, got $2)" >&2; exit 1; }; echo "ok: $1 = $2"; }

pg mkdir -p "$ARCHIVE"
echo "== primary cluster (WAL archiving on)"
pg "$PG_BIN/initdb" -D "$WORK/primary" -A trust -U postgres >/dev/null
cat >> "$WORK/primary/postgresql.conf" <<CONF
port = $PORT_PRIMARY
listen_addresses = '127.0.0.1'
unix_socket_directories = '$WORK'
wal_level = replica
archive_mode = on
archive_command = 'test ! -f $ARCHIVE/%f && cp %p $ARCHIVE/%f'
archive_timeout = 5
CONF
pg "$PG_BIN/pg_ctl" -D "$WORK/primary" -l "$WORK/primary.log" -w start >/dev/null
"$PG_BIN/psql" -h 127.0.0.1 -p "$PORT_PRIMARY" -U postgres -qAt -c "CREATE DATABASE monitoring" >/dev/null
dotnet_host "$PORT_PRIMARY" --migrate

DATA='{"kind":"synthetic-session","version":1,"sourceIp":"192.0.2.1","destinationIp":"2001:db8::2","sourcePort":0,"destinationPort":65535,"protocol":"TCP","startedAt":"2026-09-29T12:00:00Z","endedAt":"2026-09-29T12:00:00.123Z"}'
accept() { # port first last
  sql "$1" "INSERT INTO monitoring.ingestion_origin(site_id,sensor_id) VALUES ('rehearsal','sensor') ON CONFLICT DO NOTHING"
  sql "$1" "INSERT INTO monitoring.ingestion_inbox(site_id,sensor_id,event_id,batch_id,schema_version,occurred_at,occurred_at_text,data,accepted_at)
            SELECT 'rehearsal','sensor','event-'||g,'batch',1,TIMESTAMPTZ '2026-09-29 12:00:00.1+00','2026-09-29T12:00:00.100Z','$DATA'::jsonb,clock_timestamp()
            FROM generate_series($2,$3) g"
}
echo "== data before the backup"
accept "$PORT_PRIMARY" 1 3
dotnet_host "$PORT_PRIMARY" --project-pending
expect "sessions before backup" "$(sql $PORT_PRIMARY 'SELECT count(*) FROM monitoring.session_projection')" 3

echo "== base backup"
pg "$PG_BIN/pg_basebackup" -h 127.0.0.1 -p "$PORT_PRIMARY" -U postgres -D "$WORK/base" -Fp -X fetch -c fast >/dev/null

echo "== data after the backup, then a restore point, then a destructive mistake"
accept "$PORT_PRIMARY" 4 5
dotnet_host "$PORT_PRIMARY" --project-pending
sql "$PORT_PRIMARY" "SELECT pg_create_restore_point('before_mistake')" >/dev/null
sql "$PORT_PRIMARY" "DELETE FROM monitoring.session_projection"
expect "sessions after the mistake" "$(sql $PORT_PRIMARY 'SELECT count(*) FROM monitoring.session_projection')" 0
# Wait for the very segment that holds the mistake: the archiver ships segments in order, so once it is there the restore point
# before it is there too. Counting archived files is not enough, because the base backup already archived some.
segment=$(sql "$PORT_PRIMARY" "SELECT pg_walfile_name(pg_current_wal_lsn())")
sql "$PORT_PRIMARY" "SELECT pg_switch_wal()" >/dev/null
for _ in $(seq 1 60); do [ -f "$ARCHIVE/$segment" ] && break; sleep 1; done
[ -f "$ARCHIVE/$segment" ] || { echo "FAIL: WAL segment $segment was not archived in 60 s" >&2; exit 1; }

echo "== the primary is lost abruptly"
pg "$PG_BIN/pg_ctl" -D "$WORK/primary" -m immediate stop >/dev/null

echo "== recovery from the base backup + archived WAL to the restore point"
pg cp -a "$WORK/base" "$WORK/restored"
cat >> "$WORK/restored/postgresql.conf" <<CONF
port = $PORT_RESTORED
listen_addresses = '127.0.0.1'
unix_socket_directories = '$WORK'
archive_mode = off
restore_command = 'cp $ARCHIVE/%f %p'
recovery_target_name = 'before_mistake'
recovery_target_action = 'promote'
CONF
pg touch "$WORK/restored/recovery.signal"
pg "$PG_BIN/pg_ctl" -D "$WORK/restored" -l "$WORK/restored.log" -w start >/dev/null
for _ in $(seq 1 60); do [ "$(sql $PORT_RESTORED 'SELECT pg_is_in_recovery()' 2>/dev/null || echo t)" = f ] && break; sleep 1; done
expect "recovery finished" "$(sql $PORT_RESTORED 'SELECT pg_is_in_recovery()')" f
expect "sessions recovered to the restore point" "$(sql $PORT_RESTORED 'SELECT count(*) FROM monitoring.session_projection')" 5
expect "accepted events recovered" "$(sql $PORT_RESTORED 'SELECT count(*) FROM monitoring.ingestion_inbox')" 5
dotnet_host "$PORT_RESTORED" --migrate   # set -e: a non-zero exit aborts the rehearsal
echo "ok: migrations after restore ran cleanly (no-op)"

echo "== post-restore finalization: history older than the retention window must not be served"
Retention__SessionRetention=3.00:00:00 dotnet_host "$PORT_RESTORED" --restore-finalize
expect "no traffic data left" "$(sql $PORT_RESTORED 'SELECT count(*) FROM monitoring.session_projection')" 0
expect "tombstones instead" "$(sql $PORT_RESTORED "SELECT count(*) FROM monitoring.session_identity WHERE state='deleted'")" 5
expect "delete barriers published" "$(sql $PORT_RESTORED "SELECT count(*) FROM monitoring.projection_outbox WHERE payload->>'operation'='delete'")" 5
Retention__SessionRetention=3.00:00:00 dotnet_host "$PORT_RESTORED" --restore-finalize | grep -q 'expiredSessions=0'
echo "ok: finalizer is idempotent"
echo "PITR rehearsal OK"
