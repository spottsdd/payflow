require 'sinatra'
require 'json'

SERVICE_NAME = 'fraud-detection'.freeze

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

set :port, ENV.fetch('PORT', '8082').to_i
set :bind, '0.0.0.0'

FRAUD_LATENCY_MS = ENV.fetch('FRAUD_LATENCY_MS', '0').to_i

before do
  content_type :json
end

def parse_body
  JSON.parse(request.body.read)
rescue
  halt 400, { error: 'invalid json' }.to_json
end

get '/health' do
  { status: 'ok' }.to_json
end

post '/fraud/check' do
  body = parse_body
  payment_id = body['payment_id']
  amount     = body['amount'].to_f

  sleep(FRAUD_LATENCY_MS / 1000.0) if FRAUD_LATENCY_MS > 0

  result = if amount > 10_000
    { risk_score: 0.95, decision: 'DENY' }
  elsif amount > 2_000
    { risk_score: 0.65, decision: 'FLAG' }
  else
    { risk_score: 0.10, decision: 'APPROVE' }
  end

  log('info', 'fraud check complete', payment_id: payment_id, decision: result[:decision], risk_score: result[:risk_score])
  result.to_json
end
