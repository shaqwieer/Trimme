#!/usr/bin/env bash
# Builds and starts the local stack, waits for the API to report ready, then prints the result.
# Usage: infra/scripts/compose-smoke.sh [--down]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE=(docker compose -f "$ROOT/infra/docker-compose.yml")
API_URL="http://localhost:${TRIMME_API_PORT:-8080}"

"${COMPOSE[@]}" up --build -d

for attempt in $(seq 1 60); do
  if curl -fsS "$API_URL/health/ready" >/dev/null 2>&1; then
    echo "API ready after ${attempt} attempt(s):"
    curl -fsS "$API_URL/health/ready"; echo
    curl -fsS "$API_URL/api/v1/meta"; echo
    "${COMPOSE[@]}" ps
    if [[ "${1:-}" == "--down" ]]; then "${COMPOSE[@]}" down; fi
    exit 0
  fi
  sleep 2
done

echo "API did not become ready in time" >&2
"${COMPOSE[@]}" ps >&2
"${COMPOSE[@]}" logs --tail=80 migrate api >&2
exit 1
