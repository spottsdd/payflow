import os

import httpx
from fastapi import FastAPI
from fastapi.responses import JSONResponse

from app.logger import log

GATEWAY_STUB_URL = os.getenv("GATEWAY_STUB_URL", "http://gateway-stub:9999")

app = FastAPI()


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.post("/process")
async def process_payment(body: dict):
    payment_id = body.get("payment_id", "")
    amount = body.get("amount", 0)
    currency = body.get("currency", "")

    log("INFO", "processing payment", payment_id=payment_id)

    async with httpx.AsyncClient() as client:
        resp = await client.post(
            f"{GATEWAY_STUB_URL}/charge",
            json={"payment_id": payment_id, "amount": amount, "currency": currency},
        )

    data = resp.json()
    log("INFO", "gateway response", payment_id=payment_id, status=data.get("status"))
    return JSONResponse(status_code=resp.status_code, content=data)
