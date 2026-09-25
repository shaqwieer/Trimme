#!/usr/bin/env bash
# Builds and starts the local stack (PostGIS -> migrate -> API -> web), waits until the API reports ready
# and the web app serves /ar and proxies /api, then prints the result.
# Usage: infra/scripts/compose-smoke.sh [--down]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE=(docker compose -f "$ROOT/infra/docker-compose.yml")
API_URL="http://localhost:${TRIMME_API_PORT:-8080}"
WEB_URL="http://localhost:${TRIMME_WEB_PORT:-3000}"

"${COMPOSE[@]}" up --build -d

wait_for() {
  local url="$1" label="$2"
  for attempt in $(seq 1 90); do
    if curl -fsS "$url" >/dev/null 2>&1; then
      echo "$label ready after ${attempt} attempt(s)"
      return 0
    fi
    sleep 2
  done
  echo "$label did not become ready in time ($url)" >&2
  "${COMPOSE[@]}" ps >&2
  "${COMPOSE[@]}" logs --tail=80 migrate api web >&2
  return 1
}

wait_for "$API_URL/health/ready" "API"
curl -fsS "$API_URL/health/ready"; echo
wait_for "$WEB_URL/ar" "Web"
echo "Web /ar -> $(curl -s -o /dev/null -w '%{http_code}' "$WEB_URL/ar")"
echo "Web proxy /api/v1/meta -> $(curl -fsS "$WEB_URL/api/v1/meta")"
"${COMPOSE[@]}" ps

if [[ "${1:-}" == "--down" ]]; then
  "${COMPOSE[@]}" down
fi
