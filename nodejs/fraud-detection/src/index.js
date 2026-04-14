'use strict';

const express = require('express');
const { log } = require('./logger');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 8082;
const FRAUD_LATENCY_MS = parseInt(process.env.FRAUD_LATENCY_MS || '0', 10);

app.get('/health', (req, res) => {
  res.json({ status: 'ok' });
});

app.post('/fraud/check', async (req, res) => {
  const { payment_id, amount } = req.body || {};

  if (FRAUD_LATENCY_MS > 0) {
    await new Promise(r => setTimeout(r, FRAUD_LATENCY_MS));
  }

  log('info', 'fraud check', { payment_id, amount });

  if (amount > 10000) {
    return res.json({ risk_score: 0.95, decision: 'DENY' });
  }
  if (amount > 2000) {
    return res.json({ risk_score: 0.65, decision: 'FLAG' });
  }
  return res.json({ risk_score: 0.10, decision: 'APPROVE' });
});

app.listen(PORT, () => {
  log('info', `fraud-detection listening on port ${PORT}`);
});
