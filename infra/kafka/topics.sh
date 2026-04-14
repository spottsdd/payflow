#!/usr/bin/env bash
# Creates Kafka topics required by payflow.
# Run inside the Kafka container or against a running broker.
# Usage: KAFKA_BROKER=localhost:9092 bash topics.sh

set -euo pipefail

BROKER="${KAFKA_BROKER:-localhost:9092}"
PARTITIONS="${KAFKA_PARTITIONS:-3}"
REPLICATION="${KAFKA_REPLICATION_FACTOR:-1}"

TOPICS=(
  "payment.completed"
  "payment.failed"
)

echo "Creating topics on broker: $BROKER"

for topic in "${TOPICS[@]}"; do
  kafka-topics.sh \
    --bootstrap-server "$BROKER" \
    --create \
    --if-not-exists \
    --topic "$topic" \
    --partitions "$PARTITIONS" \
    --replication-factor "$REPLICATION"
  echo "Topic ready: $topic"
done

echo "All topics created."
