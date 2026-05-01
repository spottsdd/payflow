#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG_DIR="/tmp/payflow"
PID_FILE="$LOG_DIR/pids"
INFRA_COMPOSE="$SCRIPT_DIR/docker-compose.infra.yml"

if [ -f "$PID_FILE" ]; then
  echo "Stopping app services..."
  while read -r pid; do
    if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null && echo "  killed pid $pid"
    fi
  done < "$PID_FILE"
  rm -f "$PID_FILE"
fi

echo "Stopping infra containers..."
docker compose -f "$INFRA_COMPOSE" down

echo "Stopped."
