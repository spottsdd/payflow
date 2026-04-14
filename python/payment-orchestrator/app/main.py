import json
import os
import uuid
from datetime import datetime, timezone

import httpx
import psycopg2
from confluent_kafka import Producer
from fastapi import FastAPI
from fastapi.responses import JSONResponse

from app.logger import log

DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://payflow:payflow@postgres:5432/payflow")
KAFKA_BROKERS = os.getenv("KAFKA_BROKERS", "kafka:9092")
FRAUD_SERVICE_URL = os.getenv("FRAUD_SERVICE_URL", "http://fraud-detection:8082")
PROCESSOR_SERVICE_URL = os.getenv("PROCESSOR_SERVICE_URL", "http://payment-processor:8083")
SETTLEMENT_SERVICE_URL = os.getenv("SETTLEMENT_SERVICE_URL", "http://settlement-service:8084")
FRAUD_TIMEOUT = float(os.getenv("FRAUD_TIMEOUT_MS", "5000")) / 1000
PROCESSOR_TIMEOUT = float(os.getenv("PROCESSOR_TIMEOUT_MS", "10000")) / 1000
SETTLEMENT_TIMEOUT = float(os.getenv("SETTLEMENT_TIMEOUT_MS", "5000")) / 1000

app = FastAPI()

producer = Producer({"bootstrap.servers": KAFKA_BROKERS})


def delivery_report(err, msg):
    if err:
        log("ERROR", "kafka delivery failed", error=str(err))


def publish(topic: str, payload: dict):
    producer.produce(topic, json.dumps(payload).encode(), callback=delivery_report)
    producer.poll(0)


def get_conn():
    return psycopg2.connect(DATABASE_URL)


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/payments")
async def create_payment(request_body: dict):
    payment_id = str(uuid.uuid4())
    from_account_id = request_body["from_account_id"]
    to_account_id = request_body["to_account_id"]
    amount = request_body["amount"]
    currency = request_body["currency"]

    conn = get_conn()
    try:
        with conn.cursor() as cur:
            cur.execute(
                """
                INSERT INTO payments (id, from_account_id, to_account_id, amount, currency, status, created_at, updated_at)
                VALUES (%s, %s, %s, %s, %s, 'PENDING', NOW(), NOW())
                """,
                (payment_id, from_account_id, to_account_id, amount, currency),
            )
        conn.commit()
    finally:
        conn.close()

    log("INFO", "payment created", payment_id=payment_id, status="PENDING")

    def update_status(status: str):
        c = get_conn()
        try:
            with c.cursor() as cur:
                cur.execute(
                    "UPDATE payments SET status=%s, updated_at=NOW() WHERE id=%s",
                    (status, payment_id),
                )
            c.commit()
        finally:
            c.close()

    async with httpx.AsyncClient() as client:
        try:
            fraud_resp = await client.post(
                f"{FRAUD_SERVICE_URL}/fraud/check",
                json={"payment_id": payment_id, "from_account_id": from_account_id, "amount": amount, "currency": currency},
                timeout=FRAUD_TIMEOUT,
            )
        except Exception as e:
            log("ERROR", "fraud service error", payment_id=payment_id, error=str(e))
            update_status("FAILED")
            publish("payment.failed", {"payment_id": payment_id, "reason": "service_error"})
            return JSONResponse(status_code=500, content={"error": "fraud service error"})

        fraud_data = fraud_resp.json()
        if fraud_data.get("decision") == "DENY":
            update_status("DECLINED")
            publish("payment.failed", {"payment_id": payment_id, "reason": "fraud_denied"})
            log("INFO", "payment declined by fraud", payment_id=payment_id)
            return JSONResponse(status_code=200, content={"payment_id": payment_id, "status": "DECLINED", "transaction_id": ""})

        try:
            proc_resp = await client.post(
                f"{PROCESSOR_SERVICE_URL}/process",
                json={"payment_id": payment_id, "from_account_id": from_account_id, "amount": amount, "currency": currency},
                timeout=PROCESSOR_TIMEOUT,
            )
        except Exception as e:
            log("ERROR", "processor service error", payment_id=payment_id, error=str(e))
            update_status("FAILED")
            publish("payment.failed", {"payment_id": payment_id, "reason": "service_error"})
            return JSONResponse(status_code=500, content={"error": "processor service error"})

        proc_data = proc_resp.json()
        if proc_data.get("status") == "DECLINED":
            update_status("FAILED")
            publish("payment.failed", {"payment_id": payment_id, "reason": "processor_declined"})
            log("INFO", "payment declined by processor", payment_id=payment_id)
            return JSONResponse(status_code=200, content={"payment_id": payment_id, "status": "FAILED", "transaction_id": ""})

        transaction_id = proc_data.get("transaction_id", "")

        try:
            settle_resp = await client.post(
                f"{SETTLEMENT_SERVICE_URL}/settle",
                json={
                    "payment_id": payment_id,
                    "from_account_id": from_account_id,
                    "to_account_id": to_account_id,
                    "amount": amount,
                    "currency": currency,
                    "transaction_id": transaction_id,
                },
                timeout=SETTLEMENT_TIMEOUT,
            )
        except Exception as e:
            log("ERROR", "settlement service error", payment_id=payment_id, error=str(e))
            update_status("FAILED")
            publish("payment.failed", {"payment_id": payment_id, "reason": "service_error"})
            return JSONResponse(status_code=500, content={"error": "settlement service error"})

        if settle_resp.status_code == 402:
            update_status("FAILED")
            publish("payment.failed", {"payment_id": payment_id, "reason": "insufficient_funds"})
            log("INFO", "payment failed: insufficient funds", payment_id=payment_id)
            return JSONResponse(
                status_code=200,
                content={"payment_id": payment_id, "status": "FAILED", "transaction_id": transaction_id},
            )

        if settle_resp.status_code != 200:
            update_status("FAILED")
            publish("payment.failed", {"payment_id": payment_id, "reason": "service_error"})
            return JSONResponse(status_code=500, content={"error": "settlement service error"})

    update_status("COMPLETED")
    publish("payment.completed", {"payment_id": payment_id, "amount": amount, "from_account_id": from_account_id})
    log("INFO", "payment completed", payment_id=payment_id)

    return JSONResponse(
        status_code=200,
        content={"payment_id": payment_id, "status": "COMPLETED", "transaction_id": transaction_id},
    )


@app.get("/payments/{payment_id}")
def get_payment(payment_id: str):
    conn = get_conn()
    try:
        with conn.cursor() as cur:
            cur.execute(
                "SELECT id, status, amount, currency, created_at FROM payments WHERE id=%s",
                (payment_id,),
            )
            row = cur.fetchone()
    finally:
        conn.close()

    if not row:
        return JSONResponse(status_code=404, content={"error": "not found"})

    pid, status, amount, currency, created_at = row
    return {
        "payment_id": str(pid),
        "status": status,
        "amount": float(amount),
        "currency": currency,
        "created_at": created_at.isoformat() if isinstance(created_at, datetime) else str(created_at),
    }
