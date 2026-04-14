#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$SCRIPT_DIR/../docker"

docker compose -f "$COMPOSE_DIR/docker-compose.yml" up --build -d

echo "Stack started. Services:"
echo "  api-gateway:          http://localhost:8080"
echo "  payment-orchestrator: http://localhost:8081"
echo "  fraud-detection:      http://localhost:8082"
echo "  payment-processor:    http://localhost:8083"
echo "  settlement-service:   http://localhost:8084"
echo "  notification-service: http://localhost:8085"
