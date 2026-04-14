import json
import os
import threading
import uuid
from contextlib import asynccontextmanager

import psycopg2
from confluent_kafka import Consumer, KafkaError
from fastapi import FastAPI

from app.logger import log

DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://payflow:payflow@postgres:5432/payflow")
KAFKA_BROKERS = os.getenv("KAFKA_BROKERS", "kafka:9092")

TOPIC_TYPE_MAP = {
    "payment.completed": "PAYMENT_COMPLETED",
    "payment.failed": "PAYMENT_FAILED",
}

_stop_event = threading.Event()


def consume_loop():
    consumer = Consumer({
        "bootstrap.servers": KAFKA_BROKERS,
        "group.id": "notification-service",
        "auto.offset.reset": "earliest",
    })
    consumer.subscribe(list(TOPIC_TYPE_MAP.keys()))
    log("INFO", "kafka consumer started")

    while not _stop_event.is_set():
        msg = consumer.poll(timeout=1.0)
        if msg is None:
            continue
        if msg.error():
            if msg.error().code() != KafkaError._PARTITION_EOF:
                log("ERROR", "kafka error", error=str(msg.error()))
            continue

        topic = msg.topic()
        try:
            payload = json.loads(msg.value().decode())
            notification_type = TOPIC_TYPE_MAP.get(topic, "UNKNOWN")
            payment_id = payload.get("payment_id", "")

            conn = psycopg2.connect(DATABASE_URL)
            try:
                with conn.cursor() as cur:
                    cur.execute(
                        """
                        INSERT INTO notifications (id, payment_id, type, payload, created_at)
                        VALUES (%s, %s, %s, %s, NOW())
                        """,
                        (str(uuid.uuid4()), payment_id, notification_type, json.dumps(payload)),
                    )
                conn.commit()
            finally:
                conn.close()

            log("INFO", "notification saved", payment_id=payment_id, type=notification_type)
        except Exception as e:
            log("ERROR", "failed to process message", error=str(e))

    consumer.close()
    log("INFO", "kafka consumer stopped")


@asynccontextmanager
async def lifespan(app: FastAPI):
    _stop_event.clear()
    thread = threading.Thread(target=consume_loop, daemon=True)
    thread.start()
    yield
    _stop_event.set()
    thread.join(timeout=5)


app = FastAPI(lifespan=lifespan)


@app.get("/health")
async def health():
    return {"status": "ok"}
