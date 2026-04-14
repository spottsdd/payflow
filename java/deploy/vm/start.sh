#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

cd "$SCRIPT_DIR/../docker"

docker compose up --build -d

echo "Waiting for services to become healthy..."

HEALTHY_SERVICES=(
  "api-gateway"
  "payment-orchestrator"
  "fraud-detection"
  "payment-processor"
  "settlement-service"
  "notification-service"
  "postgres"
  "kafka"
)

for i in $(seq 1 60); do
  ps_output=$(docker compose ps)
  all_healthy=true

  for svc in "${HEALTHY_SERVICES[@]}"; do
    if ! echo "$ps_output" | grep "$svc" | grep -q "(healthy)"; then
      all_healthy=false
      break
    fi
  done

  if $all_healthy; then
    echo ""
    echo "All services are healthy."
    break
  fi

  printf "."
  sleep 5

  if [ "$i" -eq 60 ]; then
    echo ""
    echo "Timed out waiting for services to become healthy."
    exit 1
  fi
done

echo ""
docker compose ps
