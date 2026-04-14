'use strict';

const http = require('http');

const PORT = parseInt(process.env.PORT || '9999', 10);
const FAILURE_RATE = parseFloat(process.env.STUB_FAILURE_RATE || '0.1');

function log(level, message, extra = {}) {
  process.stdout.write(JSON.stringify({
    timestamp: new Date().toISOString(),
    level,
    service: 'gateway-stub',
    message,
    ...extra,
  }) + '\n');
}

function randomId() {
  return Math.random().toString(36).slice(2, 12).toUpperCase();
}

const server = http.createServer((req, res) => {
  if (req.method === 'GET' && req.url === '/health') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ status: 'ok' }));
    return;
  }

  if (req.method !== 'POST' || req.url !== '/charge') {
    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'not found' }));
    return;
  }

  let body = '';
  req.on('data', chunk => { body += chunk; });
  req.on('end', () => {
    let payload = {};
    try {
      payload = JSON.parse(body);
    } catch (_) {
      res.writeHead(400, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: 'invalid json' }));
      return;
    }

    const declined = Math.random() < FAILURE_RATE;
    const transactionId = randomId();
    const status = declined ? 'DECLINED' : 'SUCCESS';

    log('info', 'charge processed', {
      payment_id: payload.payment_id,
      amount: payload.amount,
      status,
      transaction_id: transactionId,
    });

    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ status, transaction_id: transactionId }));
  });
});

server.listen(PORT, () => {
  log('info', `gateway-stub listening on port ${PORT}`, {
    failure_rate: FAILURE_RATE,
  });
});
