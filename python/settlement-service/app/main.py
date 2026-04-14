import os
import uuid

import psycopg2
from fastapi import FastAPI
from fastapi.responses import JSONResponse

from app.logger import log

DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://payflow:payflow@postgres:5432/payflow")

app = FastAPI()


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/settle")
def settle(body: dict):
    payment_id = body["payment_id"]
    from_account_id = body["from_account_id"]
    to_account_id = body["to_account_id"]
    amount = body["amount"]

    conn = psycopg2.connect(DATABASE_URL)
    conn.autocommit = False
    try:
        with conn.cursor() as cur:
            cur.execute(
                "SELECT balance FROM accounts WHERE id=%s FOR UPDATE",
                (from_account_id,),
            )
            row = cur.fetchone()
            if row is None:
                conn.rollback()
                return JSONResponse(status_code=404, content={"error": "account not found"})

            balance = float(row[0])
            if balance < float(amount):
                conn.rollback()
                log("INFO", "insufficient funds", payment_id=payment_id, balance=balance, amount=float(amount))
                return JSONResponse(status_code=402, content={"error": "insufficient funds"})

            cur.execute(
                "UPDATE accounts SET balance=balance-%s WHERE id=%s",
                (amount, from_account_id),
            )
            cur.execute(
                "UPDATE accounts SET balance=balance+%s WHERE id=%s",
                (amount, to_account_id),
            )

            settlement_id = str(uuid.uuid4())
            cur.execute(
                """
                INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at)
                VALUES (%s, %s, %s, %s, %s, NOW())
                RETURNING id, settled_at
                """,
                (settlement_id, payment_id, from_account_id, to_account_id, amount),
            )
            sid, settled_at = cur.fetchone()

        conn.commit()
        log("INFO", "settlement complete", payment_id=payment_id, settlement_id=str(sid))
        return {"settlement_id": str(sid), "settled_at": settled_at.isoformat()}
    except Exception as e:
        conn.rollback()
        log("ERROR", "settlement error", payment_id=payment_id, error=str(e))
        raise
    finally:
        conn.close()
