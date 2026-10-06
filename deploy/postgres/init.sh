#!/usr/bin/env bash
set -euo pipefail
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v app_password="$(cat /run/secrets/postgres_app)" -v cdc_password="$(cat /run/secrets/postgres_cdc)" <<'SQL'
CREATE ROLE monitoring_app LOGIN PASSWORD :'app_password';
CREATE ROLE monitoring_cdc LOGIN REPLICATION PASSWORD :'cdc_password';
ALTER DATABASE monitoring OWNER TO monitoring_app;
GRANT CONNECT ON DATABASE monitoring TO monitoring_cdc;
SQL
