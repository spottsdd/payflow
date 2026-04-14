'use strict';

const express = require('express');
const { v4: uuidv4 } = require('uuid');
const { createClient } = require('./db');
const { log } = require('./logger');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 8084;

app.get('/health', (req, res) => {
  res.json({ status: 'ok' });
});

app.post('/settle', async (req, res) => {
  const { payment_id, from_account_id, to_account_id, amount } = req.body || {};

  const client = createClient();
  try {
    await client.connect();
    await client.query('BEGIN');

    const { rows } = await client.query(
      'SELECT balance FROM accounts WHERE id=$1 FOR UPDATE',
      [from_account_id]
    );

    if (rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ error: 'account not found' });
    }

    if (parseFloat(rows[0].balance) < parseFloat(amount)) {
      await client.query('ROLLBACK');
      return res.status(402).json({ error: 'insufficient funds' });
    }

    await client.query(
      'UPDATE accounts SET balance=balance-$1 WHERE id=$2',
      [amount, from_account_id]
    );
    await client.query(
      'UPDATE accounts SET balance=balance+$1 WHERE id=$2',
      [amount, to_account_id]
    );

    const { rows: inserted } = await client.query(
      'INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at) VALUES ($1,$2,$3,$4,$5,NOW()) RETURNING id, settled_at',
      [uuidv4(), payment_id, from_account_id, to_account_id, amount]
    );

    await client.query('COMMIT');

    const { id: settlement_id, settled_at } = inserted[0];
    log('info', 'settlement complete', { payment_id, settlement_id });
    res.json({ settlement_id, settled_at });
  } catch (err) {
    await client.query('ROLLBACK').catch(() => {});
    log('error', 'settlement error', { payment_id, error: err.message });
    res.status(500).json({ error: 'settlement failed' });
  } finally {
    await client.end().catch(() => {});
  }
});

app.listen(PORT, () => {
  log('info', `settlement-service listening on port ${PORT}`);
});
