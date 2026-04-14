require 'sinatra'
require 'json'
require 'pg'
require 'kafka'
require 'securerandom'
require 'uri'

SERVICE_NAME = 'notification-service'.freeze

def log(level, message, **extra)
  entry = {
    timestamp: Time.now.utc.iso8601(3),
    level: level,
    service: SERVICE_NAME,
    message: message,
    trace_id: '',
    span_id: '',
  }.merge(extra)
  $stdout.puts entry.to_json
  $stdout.flush
end

set :port, ENV.fetch('PORT', '8085').to_i
set :bind, '0.0.0.0'

before do
  content_type :json
end

def db
  @db ||= begin
    uri = URI.parse(ENV.fetch('DATABASE_URL', 'postgresql://payflow:payflow@postgres:5432/payflow'))
    PG.connect(
      host:     uri.host,
      port:     uri.port || 5432,
      dbname:   uri.path.delete_prefix('/'),
      user:     uri.user,
      password: uri.password
    )
  end
end

TOPIC_TO_TYPE = {
  'payment.completed' => 'PAYMENT_COMPLETED',
  'payment.failed'    => 'PAYMENT_FAILED',
}.freeze

Thread.new do
  brokers  = ENV.fetch('KAFKA_BROKERS', 'kafka:9092').split(',')
  kafka    = Kafka.new(brokers)
  consumer = kafka.consumer(group_id: 'notification-service')
  consumer.subscribe('payment.completed')
  consumer.subscribe('payment.failed')

  log('info', 'kafka consumer started')

  consumer.each_message do |message|
    payload = JSON.parse(message.value)
    type    = TOPIC_TO_TYPE[message.topic] || 'UNKNOWN'

    db.exec_params(
      'INSERT INTO notifications (id, payment_id, type, payload, created_at) VALUES ($1,$2,$3,$4,NOW())',
      [SecureRandom.uuid, payload['payment_id'], type, message.value]
    )
    log('info', 'notification stored', type: type, payment_id: payload['payment_id'])
  rescue => e
    log('error', 'failed to process message', error: e.message, topic: message.topic)
  end
rescue => e
  log('error', 'kafka consumer error', error: e.message)
end

get '/health' do
  { status: 'ok' }.to_json
end
