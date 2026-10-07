#!/usr/bin/env bash
# Fetches the pinned Kafka client API jars the pure Connect transform is compiled and tested against, without Docker.
# build-transform.ps1 copies the same Kafka version from the pinned image; this script is the container-free equivalent.
set -euo pipefail
destination="${1:-deploy/connect/transforms/build/libs}"
mirror="https://repo.maven.apache.org/maven2"
mkdir -p "$destination"
fetch() { # name url sha256
  local file="$destination/$1"
  if [[ ! -f "$file" ]] || ! echo "$3  $file" | sha256sum -c --quiet - 2>/dev/null; then
    curl -fsSL --retry 5 --retry-delay 3 --retry-all-errors -o "$file" "$2"
  fi
  echo "$3  $file" | sha256sum -c --quiet - || { echo "Checksum mismatch for $1" >&2; rm -f "$file"; exit 1; }
}
fetch connect-api-4.3.1.jar "$mirror/org/apache/kafka/connect-api/4.3.1/connect-api-4.3.1.jar" 5ce6158d453d2d84ab3509260fff872c67fb532a51519da6acafc6835bc48cfe
fetch kafka-clients-4.3.1.jar "$mirror/org/apache/kafka/kafka-clients/4.3.1/kafka-clients-4.3.1.jar" 52501b7b47510c66f898871adaf6d2968ab7246561d44ced43643a8a587f0b36
fetch slf4j-api-2.0.17.jar "$mirror/org/slf4j/slf4j-api/2.0.17/slf4j-api-2.0.17.jar" 7b751d952061954d5abfed7181c1f645d336091b679891591d63329c622eb832
