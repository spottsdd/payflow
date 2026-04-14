'use strict';

const SERVICE_NAME = 'api-gateway';

function log(level, message, extra = {}) {
  const entry = {
    timestamp: new Date().toISOString(),
    level,
    service: SERVICE_NAME,
    message,
    trace_id: '',
    span_id: '',
    ...extra,
  };
  process.stdout.write(JSON.stringify(entry) + '\n');
}

module.exports = { log };
