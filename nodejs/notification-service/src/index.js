'use strict';

const express = require('express');
const { Kafka } = require('kafkajs');
const { Pool } = require('pg');
const { v4: uuidv4 } = require('uuid');
const { log } = require('./logger');

const app = express();

const PORT = process.env.PORT || 8085;
const KAFKA_BROKERS = (process.env.KAFKA_BROKERS || 'kafka:9092').split(',');

const pool = new Pool({ connectionString: process.env.DATABASE_URL });

const kafka = new Kafka({ clientId: 'notification-service', brokers: KAFKA_BROKERS });
const consumer = kafka.consumer({ groupId: 'notification-service' });

const TYPE_MAP = {
  'payment.completed': 'PAYMENT_COMPLETED',
  'payment.failed': 'PAYMENT_FAILED',
};

async function startConsumer() {
  await consumer.connect();
  await consumer.subscribe({ topics: ['payment.completed', 'payment.failed'] });

  await consumer.run({
    eachMessage: async ({ topic, message }) => {
      let payload;
      try {
        payload = JSON.parse(message.value.toString());
      } catch (e) {
        log('error', 'failed to parse kafka message', { topic, error: e.message });
        return;
      }

      const type = TYPE_MAP[topic];
      const payment_id = payload.payment_id;

      try {
        await pool.query(
          'INSERT INTO notifications (id, payment_id, type, payload, created_at) VALUES ($1,$2,$3,$4,NOW())',
          [uuidv4(), payment_id, type, JSON.stringify(payload)]
        );
        log('info', 'notification saved', { payment_id, type });
      } catch (err) {
        log('error', 'failed to save notification', { payment_id, error: err.message });
      }
    },
  });
}

app.get('/health', (req, res) => {
  res.json({ status: 'ok' });
});

app.listen(PORT, () => {
  log('info', `notification-service listening on port ${PORT}`);
  startConsumer().catch(err => {
    log('error', 'consumer startup failed', { error: err.message });
  });
});
