#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$SCRIPT_DIR/../docker"

echo "Starting payflow Ruby stack..."
docker compose -f "$COMPOSE_DIR/docker-compose.yml" up --build -d

echo "Waiting for services to become healthy..."
docker compose -f "$COMPOSE_DIR/docker-compose.yml" wait

echo "All services running."
echo "  api-gateway:          http://localhost:8080"
echo "  payment-orchestrator: http://localhost:8081"
echo "  fraud-detection:      http://localhost:8082"
echo "  payment-processor:    http://localhost:8083"
echo "  settlement-service:   http://localhost:8084"
echo "  notification-service: http://localhost:8085"
