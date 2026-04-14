#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/../docker/docker-compose.yml"

echo "Starting payflow stack..."
docker compose -f "$COMPOSE_FILE" pull --ignore-buildable
docker compose -f "$COMPOSE_FILE" up --build -d

echo "Waiting for services to become healthy..."
docker compose -f "$COMPOSE_FILE" wait api-gateway 2>/dev/null || true

echo "Stack status:"
docker compose -f "$COMPOSE_FILE" ps

echo ""
echo "API Gateway available at http://localhost:8080"
echo "Run 'docker compose -f $COMPOSE_FILE logs -f' to tail logs"
