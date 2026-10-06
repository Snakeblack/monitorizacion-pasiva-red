#!/usr/bin/env bash
set -euo pipefail
umask 077
printf 'password=%s\n' "$(cat /run/secrets/postgres_cdc)" > /tmp/monitoring-cdc.properties
exec /opt/kafka/bin/connect-distributed.sh /opt/monitoring/connect.properties
