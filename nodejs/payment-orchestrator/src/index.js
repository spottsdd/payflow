'use strict';

const express = require('express');
const axios = require('axios');
const { v4: uuidv4 } = require('uuid');
const pool = require('./db');
const kafka = require('./kafka');
const { log } = require('./logger');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 8081;
const FRAUD_SERVICE_URL = process.env.FRAUD_SERVICE_URL || 'http://fraud-detection:8082';
const PROCESSOR_SERVICE_URL = process.env.PROCESSOR_SERVICE_URL || 'http://payment-processor:8083';
const SETTLEMENT_SERVICE_URL = process.env.SETTLEMENT_SERVICE_URL || 'http://settlement-service:8084';
const FRAUD_TIMEOUT_MS = parseInt(process.env.FRAUD_TIMEOUT_MS || '5000', 10);
const PROCESSOR_TIMEOUT_MS = parseInt(process.env.PROCESSOR_TIMEOUT_MS || '10000', 10);
const SETTLEMENT_TIMEOUT_MS = parseInt(process.env.SETTLEMENT_TIMEOUT_MS || '5000', 10);

app.get('/health', (req, res) => {
  res.json({ status: 'ok' });
});

async function updatePaymentStatus(id, status) {
  await pool.query(
    'UPDATE payments SET status=$1, updated_at=NOW() WHERE id=$2',
    [status, id]
  );
}

app.post('/payments', async (req, res) => {
  const { from_account_id, to_account_id, amount, currency } = req.body || {};
  const payment_id = uuidv4();

  try {
    await pool.query(
      'INSERT INTO payments (id, from_account_id, to_account_id, amount, currency, status, created_at, updated_at) VALUES ($1,$2,$3,$4,$5,$6,NOW(),NOW())',
      [payment_id, from_account_id, to_account_id, amount, currency, 'PENDING']
    );
  } catch (err) {
    log('error', 'failed to create payment', { error: err.message });
    return res.status(500).json({ error: 'failed to create payment' });
  }

  log('info', 'payment created', { payment_id });

  let fraudResult;
  try {
    const { data } = await axios.post(
      `${FRAUD_SERVICE_URL}/fraud/check`,
      { payment_id, from_account_id, amount, currency },
      { timeout: FRAUD_TIMEOUT_MS }
    );
    fraudResult = data;
  } catch (err) {
    log('error', 'fraud check failed', { payment_id, error: err.message });
    await updatePaymentStatus(payment_id, 'FAILED');
    await kafka.produce('payment.failed', { payment_id });
    return res.status(500).json({ error: 'fraud check failed' });
  }

  if (fraudResult.decision === 'DENY') {
    await updatePaymentStatus(payment_id, 'DECLINED');
    await kafka.produce('payment.failed', { payment_id });
    return res.json({ payment_id, status: 'DECLINED', transaction_id: '' });
  }

  let processorResult;
  try {
    const { data } = await axios.post(
      `${PROCESSOR_SERVICE_URL}/process`,
      { payment_id, from_account_id, amount, currency },
      { timeout: PROCESSOR_TIMEOUT_MS }
    );
    processorResult = data;
  } catch (err) {
    log('error', 'processor failed', { payment_id, error: err.message });
    await updatePaymentStatus(payment_id, 'FAILED');
    await kafka.produce('payment.failed', { payment_id });
    return res.status(500).json({ error: 'processor failed' });
  }

  if (processorResult.status === 'DECLINED') {
    await updatePaymentStatus(payment_id, 'FAILED');
    await kafka.produce('payment.failed', { payment_id, reason: 'processor_declined' });
    return res.json({ payment_id, status: 'FAILED', transaction_id: '' });
  }

  const { transaction_id } = processorResult;

  try {
    await axios.post(
      `${SETTLEMENT_SERVICE_URL}/settle`,
      { payment_id, from_account_id, to_account_id, amount, currency, transaction_id },
      { timeout: SETTLEMENT_TIMEOUT_MS }
    );
  } catch (err) {
    if (err.response && err.response.status === 402) {
      await updatePaymentStatus(payment_id, 'FAILED');
      await kafka.produce('payment.failed', { payment_id, reason: 'insufficient_funds' });
      return res.json({ payment_id, status: 'FAILED', transaction_id });
    }
    log('error', 'settlement failed', { payment_id, error: err.message });
    await updatePaymentStatus(payment_id, 'FAILED');
    await kafka.produce('payment.failed', { payment_id });
    return res.status(500).json({ error: 'settlement failed' });
  }

  await updatePaymentStatus(payment_id, 'COMPLETED');
  await kafka.produce('payment.completed', { payment_id, transaction_id });

  log('info', 'payment completed', { payment_id, transaction_id });
  res.json({ payment_id, status: 'COMPLETED', transaction_id });
});

app.get('/payments/:id', async (req, res) => {
  try {
    const { rows } = await pool.query(
      'SELECT * FROM payments WHERE id=$1',
      [req.params.id]
    );
    if (rows.length === 0) {
      return res.status(404).json({ error: 'not found' });
    }
    const p = rows[0];
    res.json({
      payment_id: p.id,
      status: p.status,
      amount: p.amount,
      currency: p.currency,
      created_at: p.created_at,
    });
  } catch (err) {
    log('error', 'failed to fetch payment', { id: req.params.id, error: err.message });
    res.status(500).json({ error: 'internal error' });
  }
});

async function start() {
  await kafka.connect();
  app.listen(PORT, () => {
    log('info', `payment-orchestrator listening on port ${PORT}`);
  });
}

start().catch(err => {
  log('error', 'startup failed', { error: err.message });
  process.exit(1);
});
