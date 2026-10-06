#!/usr/bin/env bash
set -euo pipefail
export ConnectionStrings__Monitoring="Host=postgres;Port=5432;Database=monitoring;Username=monitoring_app;Password=$(cat /run/secrets/postgres_app);Maximum Pool Size=20;Timeout=5;Command Timeout=10"
exec dotnet /app/Monitoring.Host.dll "$@"
