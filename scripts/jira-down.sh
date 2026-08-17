#!/usr/bin/env bash
# Tear down the local harness, volumes included. No state carries between runs.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$(cd "$HERE/.." && pwd)/tests/harness/docker-compose.yml"

docker compose -f "$COMPOSE_FILE" down -v
