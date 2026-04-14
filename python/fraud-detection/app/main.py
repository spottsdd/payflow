import asyncio
import os

from fastapi import FastAPI

from app.logger import log

FRAUD_LATENCY_MS = float(os.getenv("FRAUD_LATENCY_MS", "0"))

app = FastAPI()


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.post("/fraud/check")
async def fraud_check(body: dict):
    if FRAUD_LATENCY_MS > 0:
        await asyncio.sleep(FRAUD_LATENCY_MS / 1000)

    amount = body.get("amount", 0)
    payment_id = body.get("payment_id", "")

    if amount > 10000:
        result = {"risk_score": 0.95, "decision": "DENY"}
    elif amount > 2000:
        result = {"risk_score": 0.65, "decision": "FLAG"}
    else:
        result = {"risk_score": 0.10, "decision": "APPROVE"}

    log("INFO", "fraud check complete", payment_id=payment_id, decision=result["decision"])
    return result
