#!/usr/bin/env bash
set -euo pipefail
export PGPASSWORD="$(cat /run/secrets/postgres_admin)"
psql -v ON_ERROR_STOP=1 -h postgres -U monitoring -d monitoring <<'SQL'
GRANT USAGE ON SCHEMA monitoring TO monitoring_cdc;
GRANT SELECT ON monitoring.projection_outbox TO monitoring_cdc;
SELECT 'CREATE PUBLICATION monitoring_outbox FOR TABLE monitoring.projection_outbox'
WHERE NOT EXISTS (SELECT 1 FROM pg_publication WHERE pubname='monitoring_outbox')\gexec
SQL
