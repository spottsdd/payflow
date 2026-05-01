#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LANG_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
LOG_DIR="/tmp/payflow"
PID_FILE="$LOG_DIR/pids"
INFRA_COMPOSE="$SCRIPT_DIR/docker-compose.infra.yml"

mkdir -p "$LOG_DIR"
: > "$PID_FILE"

cleanup() {
  echo ""
  echo "Shutting down..."
  if [ -f "$PID_FILE" ]; then
    while read -r pid; do
      [ -n "$pid" ] && kill "$pid" 2>/dev/null || true
    done < "$PID_FILE"
  fi
  docker compose -f "$INFRA_COMPOSE" down 2>/dev/null || true
  echo "Stopped."
}
trap cleanup EXIT INT TERM

echo "Starting infra (postgres, kafka, gateway-stub, traffic-generator)..."
docker compose -f "$INFRA_COMPOSE" up -d --build postgres kafka gateway-stub

echo "Waiting for postgres + kafka + gateway-stub to be healthy..."
for i in $(seq 1 60); do
  if docker compose -f "$INFRA_COMPOSE" ps postgres kafka gateway-stub | grep -q "(healthy)" \
    && [ "$(docker compose -f "$INFRA_COMPOSE" ps postgres kafka gateway-stub | grep -c "(healthy)")" -eq 3 ]; then
    echo "Infra ready."
    break
  fi
  printf "."
  sleep 2
  if [ "$i" -eq 60 ]; then
    echo ""
    echo "Timed out waiting for infra."
    exit 1
  fi
done

SERVICES=(api-gateway payment-orchestrator fraud-detection payment-processor settlement-service notification-service)

echo "Building services with Maven..."
for svc in "${SERVICES[@]}"; do
  echo "  building $svc..."
  (cd "$LANG_DIR/$svc" && mvn package -DskipTests -q)
done

export DB_HOST=localhost
export DB_NAME=payflow
export DB_USER=payflow
export DB_PASSWORD=payflow
export KAFKA_BROKERS=localhost:9092
export GATEWAY_STUB_URL=http://localhost:9999
export FRAUD_SERVICE_URL=http://localhost:8082
export PROCESSOR_SERVICE_URL=http://localhost:8083
export SETTLEMENT_SERVICE_URL=http://localhost:8084
export ORCHESTRATOR_URL=http://localhost:8081
export FRAUD_TIMEOUT_MS=5000
export PROCESSOR_TIMEOUT_MS=10000
export SETTLEMENT_TIMEOUT_MS=5000
export FRAUD_LATENCY_MS=0

start_service() {
  local svc=$1
  local port=$2
  local jar
  jar=$(find "$LANG_DIR/$svc/target" -maxdepth 1 -name "*.jar" -not -name "*sources*" -not -name "*javadoc*" | head -1)
  if [ -z "$jar" ]; then
    echo "ERROR: no jar found for $svc"
    exit 1
  fi
  SERVER_PORT=$port nohup java -jar "$jar" >"$LOG_DIR/$svc.log" 2>&1 &
  echo $! >> "$PID_FILE"
  echo "  started $svc (pid $!) on port $port -> $LOG_DIR/$svc.log"
}

echo "Starting services..."
start_service fraud-detection 8082
start_service payment-processor 8083
start_service settlement-service 8084
start_service notification-service 8085
sleep 2
start_service payment-orchestrator 8081
sleep 2
start_service api-gateway 8080

echo "Waiting for all /health endpoints..."
for port in 8082 8083 8084 8085 8081 8080; do
  for i in $(seq 1 60); do
    if curl -sf "http://localhost:$port/health" >/dev/null 2>&1; then
      echo "  port $port: ready"
      break
    fi
    sleep 2
    if [ "$i" -eq 60 ]; then
      echo "  port $port: TIMEOUT"
      exit 1
    fi
  done
done

echo "Starting traffic-generator..."
docker compose -f "$INFRA_COMPOSE" up -d traffic-generator

echo ""
echo "All services running. Logs in $LOG_DIR"
echo "  api-gateway:          http://localhost:8080"
echo "  payment-orchestrator: http://localhost:8081"
echo "  fraud-detection:      http://localhost:8082"
echo "  payment-processor:    http://localhost:8083"
echo "  settlement-service:   http://localhost:8084"
echo "  notification-service: http://localhost:8085"
echo ""
echo "Press Ctrl+C to stop everything."
trap - EXIT
trap cleanup INT TERM
wait
