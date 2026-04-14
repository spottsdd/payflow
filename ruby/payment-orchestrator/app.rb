require 'sinatra'
require 'json'
require 'faraday'
require 'pg'
require 'kafka'
require 'securerandom'
require 'uri'

SERVICE_NAME = 'payment-orchestrator'.freeze

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

set :port, ENV.fetch('PORT', '8081').to_i
set :bind, '0.0.0.0'

FRAUD_SERVICE_URL      = ENV.fetch('FRAUD_SERVICE_URL', 'http://fraud-detection:8082').freeze
PROCESSOR_SERVICE_URL  = ENV.fetch('PROCESSOR_SERVICE_URL', 'http://payment-processor:8083').freeze
SETTLEMENT_SERVICE_URL = ENV.fetch('SETTLEMENT_SERVICE_URL', 'http://settlement-service:8084').freeze
FRAUD_TIMEOUT          = ENV.fetch('FRAUD_TIMEOUT_MS', '5000').to_i / 1000.0
PROCESSOR_TIMEOUT      = ENV.fetch('PROCESSOR_TIMEOUT_MS', '10000').to_i / 1000.0
SETTLEMENT_TIMEOUT     = ENV.fetch('SETTLEMENT_TIMEOUT_MS', '5000').to_i / 1000.0

before do
  content_type :json
end

def parse_body
  JSON.parse(request.body.read)
rescue
  halt 400, { error: 'invalid json' }.to_json
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

def kafka_producer
  @kafka_producer ||= begin
    brokers = ENV.fetch('KAFKA_BROKERS', 'kafka:9092').split(',')
    Kafka.new(brokers).producer
  end
end

def produce(topic, payload)
  kafka_producer.produce(payload.to_json, topic: topic)
  kafka_producer.deliver_messages
rescue => e
  log('warn', 'kafka produce failed', topic: topic, error: e.message)
end

def update_payment_status(id, status)
  db.exec_params(
    'UPDATE payments SET status=$1, updated_at=NOW() WHERE id=$2',
    [status, id]
  )
end

def http_client(url, timeout)
  Faraday.new(url: url) do |f|
    f.options.timeout      = timeout
    f.options.open_timeout = timeout
    f.request :json
  end
end

get '/health' do
  { status: 'ok' }.to_json
end

post '/payments' do
  body = parse_body
  payment_id     = SecureRandom.uuid
  from_account   = body['from_account_id']
  to_account     = body['to_account_id']
  amount         = body['amount']
  currency       = body['currency']

  db.exec_params(
    'INSERT INTO payments (id, from_account_id, to_account_id, amount, currency, status, created_at, updated_at) VALUES ($1,$2,$3,$4,$5,$6,NOW(),NOW())',
    [payment_id, from_account, to_account, amount, currency, 'PENDING']
  )
  log('info', 'payment created', payment_id: payment_id)

  # Fraud check
  begin
    fraud_resp = http_client(FRAUD_SERVICE_URL, FRAUD_TIMEOUT).post(
      '/fraud/check',
      { payment_id: payment_id, from_account_id: from_account, amount: amount, currency: currency }.to_json,
      'Content-Type' => 'application/json'
    )
    fraud = JSON.parse(fraud_resp.body)
  rescue => e
    log('error', 'fraud check failed', payment_id: payment_id, error: e.message)
    update_payment_status(payment_id, 'FAILED')
    produce('payment.failed', { payment_id: payment_id, reason: 'fraud_service_error' })
    halt 500, { error: 'fraud service unavailable' }.to_json
  end

  if fraud['decision'] == 'DENY'
    log('info', 'payment declined by fraud', payment_id: payment_id, risk_score: fraud['risk_score'])
    update_payment_status(payment_id, 'DECLINED')
    produce('payment.failed', { payment_id: payment_id, reason: 'fraud_denied' })
    return { payment_id: payment_id, status: 'DECLINED', transaction_id: '' }.to_json
  end

  # Payment processing
  begin
    proc_resp = http_client(PROCESSOR_SERVICE_URL, PROCESSOR_TIMEOUT).post(
      '/process',
      { payment_id: payment_id, from_account_id: from_account, amount: amount, currency: currency }.to_json,
      'Content-Type' => 'application/json'
    )
    processed = JSON.parse(proc_resp.body)
  rescue => e
    log('error', 'processor failed', payment_id: payment_id, error: e.message)
    update_payment_status(payment_id, 'FAILED')
    produce('payment.failed', { payment_id: payment_id, reason: 'processor_error' })
    halt 500, { error: 'payment processor unavailable' }.to_json
  end

  if processed['status'] == 'DECLINED'
    log('info', 'payment declined by processor', payment_id: payment_id)
    update_payment_status(payment_id, 'FAILED')
    produce('payment.failed', { payment_id: payment_id, reason: 'processor_declined' })
    return { payment_id: payment_id, status: 'FAILED', transaction_id: '' }.to_json
  end

  transaction_id = processed['transaction_id']

  # Settlement
  begin
    settle_conn = http_client(SETTLEMENT_SERVICE_URL, SETTLEMENT_TIMEOUT)
    settle_resp = settle_conn.post(
      '/settle',
      { payment_id: payment_id, from_account_id: from_account, to_account_id: to_account,
        amount: amount, currency: currency, transaction_id: transaction_id }.to_json,
      'Content-Type' => 'application/json'
    )
  rescue => e
    log('error', 'settlement failed', payment_id: payment_id, error: e.message)
    update_payment_status(payment_id, 'FAILED')
    produce('payment.failed', { payment_id: payment_id, reason: 'settlement_error' })
    halt 500, { error: 'settlement service unavailable' }.to_json
  end

  if settle_resp.status == 402
    log('info', 'settlement failed: insufficient funds', payment_id: payment_id)
    update_payment_status(payment_id, 'FAILED')
    produce('payment.failed', { payment_id: payment_id, reason: 'insufficient_funds' })
    return { payment_id: payment_id, status: 'FAILED', transaction_id: transaction_id }.to_json
  end

  update_payment_status(payment_id, 'COMPLETED')
  produce('payment.completed', { payment_id: payment_id, transaction_id: transaction_id, amount: amount, currency: currency })
  log('info', 'payment completed', payment_id: payment_id, transaction_id: transaction_id)

  { payment_id: payment_id, status: 'COMPLETED', transaction_id: transaction_id }.to_json
end

get '/payments/:id' do
  result = db.exec_params('SELECT * FROM payments WHERE id=$1', [params[:id]])

  if result.ntuples == 0
    halt 404, { error: 'not found' }.to_json
  end

  row = result[0]
  {
    payment_id:  row['id'],
    status:      row['status'],
    amount:      row['amount'],
    currency:    row['currency'],
    created_at:  row['created_at'],
  }.to_json
end
