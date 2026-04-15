'use strict';

const express = require('express');
const axios = require('axios');
const { log } = require('./logger');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 8080;
const ORCHESTRATOR_URL = process.env.ORCHESTRATOR_URL || 'http://payment-orchestrator:8081';

app.get('/health', (req, res) => {
  res.json({ status: 'ok' });
});

app.post('/payments', async (req, res) => {
  const { from_account_id, to_account_id, amount, currency } = req.body || {};

  if (!from_account_id || !to_account_id || amount == null || !currency) {
    return res.status(400).json({ error: 'from_account_id, to_account_id, amount, and currency are required' });
  }
  if (typeof amount !== 'number' || amount <= 0) {
    return res.status(400).json({ error: 'amount must be a positive number' });
  }
  if (typeof currency !== 'string' || currency.length !== 3) {
    return res.status(400).json({ error: 'currency must be a 3-character code' });
  }
  if (from_account_id === to_account_id) {
    return res.status(400).json({ error: 'from_account_id and to_account_id must be different' });
  }

  try {
    const response = await axios.post(`${ORCHESTRATOR_URL}/payments`, req.body, { timeout: 30000 });
    res.status(response.status).json(response.data);
  } catch (err) {
    if (err.response) {
      return res.status(err.response.status).json(err.response.data);
    }
    log('error', 'orchestrator unreachable', { error: err.message });
    res.status(502).json({ error: 'orchestrator unreachable' });
  }
});

app.get('/payments/:id', async (req, res) => {
  try {
    const response = await axios.get(`${ORCHESTRATOR_URL}/payments/${req.params.id}`, { timeout: 30000 });
    res.status(response.status).json(response.data);
  } catch (err) {
    if (err.response) {
      return res.status(err.response.status).json(err.response.data);
    }
    log('error', 'orchestrator unreachable', { error: err.message });
    res.status(502).json({ error: 'orchestrator unreachable' });
  }
});

app.listen(PORT, () => {
  log('info', `api-gateway listening on port ${PORT}`);
});
