'use strict';

const { Client } = require('pg');

function createClient() {
  return new Client({
    connectionString: process.env.DATABASE_URL,
  });
}

module.exports = { createClient };
