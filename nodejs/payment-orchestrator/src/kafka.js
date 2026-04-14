'use strict';

const { Kafka } = require('kafkajs');

const KAFKA_BROKERS = (process.env.KAFKA_BROKERS || 'kafka:9092').split(',');

const kafka = new Kafka({ clientId: 'payment-orchestrator', brokers: KAFKA_BROKERS });
const producer = kafka.producer();

async function connect() {
  await producer.connect();
}

async function produce(topic, message) {
  await producer.send({
    topic,
    messages: [{ value: JSON.stringify(message) }],
  });
}

module.exports = { connect, produce };
