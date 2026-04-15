require 'sinatra'
require 'json'
require 'faraday'

SERVICE_NAME = 'api-gateway'.freeze

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

set :port, ENV.fetch('PORT', '8080').to_i
set :bind, '0.0.0.0'

ORCHESTRATOR_URL = ENV.fetch('ORCHESTRATOR_URL', 'http://payment-orchestrator:8081').freeze

before do
  content_type :json
end

def parse_body
  JSON.parse(request.body.read)
rescue
  halt 400, { error: 'invalid json' }.to_json
end

def orchestrator
  Faraday.new(url: ORCHESTRATOR_URL) do |f|
    f.request :json
    f.response :raise_error
    f.options.timeout = 30
    f.options.open_timeout = 5
  end
end

get '/health' do
  { status: 'ok' }.to_json
end

post '/payments' do
  body = parse_body

  from_id   = body['from_account_id'].to_s.strip
  to_id     = body['to_account_id'].to_s.strip
  amount    = body['amount']
  currency  = body['currency'].to_s.strip

  errors = []
  errors << 'from_account_id is required' if from_id.empty?
  errors << 'to_account_id is required'   if to_id.empty?
  errors << 'amount must be greater than 0' unless amount.is_a?(Numeric) && amount > 0
  errors << 'currency must be 3 characters' unless currency.length == 3
  errors << 'from_account_id and to_account_id must differ' if !from_id.empty? && from_id == to_id

  unless errors.empty?
    halt 400, { error: errors.join(', ') }.to_json
  end

  log('info', 'forwarding payment request', from: from_id, to: to_id, amount: amount)

  begin
    resp = orchestrator.post('/payments', body.to_json, 'Content-Type' => 'application/json')
    status resp.status
    resp.body
  rescue Faraday::Error => e
    log('error', 'orchestrator error', error: e.message)
    status 502
    { error: 'upstream error' }.to_json
  end
end

get '/payments/:id' do
  payment_id = params[:id]
  log('info', 'fetching payment', payment_id: payment_id)

  begin
    resp = orchestrator.get("/payments/#{payment_id}")
    status resp.status
    resp.body
  rescue Faraday::ResourceNotFound
    status 404
    { error: 'not found' }.to_json
  rescue Faraday::Error => e
    log('error', 'orchestrator error', error: e.message)
    status 502
    { error: 'upstream error' }.to_json
  end
end
