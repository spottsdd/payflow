import os

import httpx
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.logger import log

ORCHESTRATOR_URL = os.getenv("ORCHESTRATOR_URL", "http://payment-orchestrator:8081")

app = FastAPI()


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.post("/payments")
async def create_payment(request: Request):
    body = await request.json()

    from_account_id = body.get("from_account_id")
    to_account_id = body.get("to_account_id")
    amount = body.get("amount")
    currency = body.get("currency")

    if not from_account_id:
        return JSONResponse(status_code=400, content={"error": "from_account_id is required"})
    if not to_account_id:
        return JSONResponse(status_code=400, content={"error": "to_account_id is required"})
    if amount is None:
        return JSONResponse(status_code=400, content={"error": "amount is required"})
    if not isinstance(amount, (int, float)) or amount <= 0:
        return JSONResponse(status_code=400, content={"error": "amount must be greater than 0"})
    if not currency:
        return JSONResponse(status_code=400, content={"error": "currency is required"})
    if not isinstance(currency, str) or len(currency) != 3:
        return JSONResponse(status_code=400, content={"error": "currency must be 3 characters"})
    if from_account_id == to_account_id:
        return JSONResponse(status_code=400, content={"error": "from_account_id and to_account_id must differ"})

    log("INFO", "forwarding payment request", from_account_id=from_account_id, amount=amount)

    async with httpx.AsyncClient(timeout=30.0) as client:
        resp = await client.post(f"{ORCHESTRATOR_URL}/payments", json=body)

    return JSONResponse(status_code=resp.status_code, content=resp.json())


@app.get("/payments/{payment_id}")
async def get_payment(payment_id: str):
    async with httpx.AsyncClient(timeout=30.0) as client:
        resp = await client.get(f"{ORCHESTRATOR_URL}/payments/{payment_id}")

    return JSONResponse(status_code=resp.status_code, content=resp.json())
