#!/usr/bin/env bash
set -euo pipefail
exec 3<>/dev/tcp/localhost/8080
printf 'GET /health/live HTTP/1.0\r\n\r\n' >&3
read -r response <&3
[[ "$response" == *" 200 "* ]]
