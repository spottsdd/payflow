'use strict';

const express = require('express');
const axios = require('axios');
const { log } = require('./logger');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 8083;
const GATEWAY_STUB_URL = process.env.GATEWAY_STUB_URL || 'http://gateway-stub:9999';

app.get('/health', (req, res) => {
  res.json({ status: 'ok' });
});

app.post('/process', async (req, res) => {
  const { payment_id, from_account_id, amount, currency } = req.body || {};

  try {
    const response = await axios.post(`${GATEWAY_STUB_URL}/charge`, {
      payment_id,
      amount,
      currency,
    });
    log('info', 'gateway response', { payment_id, status: response.data.status });
    res.json({ status: response.data.status, transaction_id: response.data.transaction_id });
  } catch (err) {
    log('error', 'gateway error', { payment_id, error: err.message });
    if (err.response) {
      return res.status(err.response.status).json(err.response.data);
    }
    res.status(502).json({ error: 'gateway unreachable' });
  }
});

app.listen(PORT, () => {
  log('info', `payment-processor listening on port ${PORT}`);
});
