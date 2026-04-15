require 'sinatra'
require 'json'
require 'faraday'

SERVICE_NAME = 'payment-processor'.freeze

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

set :port, ENV.fetch('PORT', '8083').to_i
set :bind, '0.0.0.0'

GATEWAY_STUB_URL = ENV.fetch('GATEWAY_STUB_URL', 'http://gateway-stub:9999').freeze

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

post '/process' do
  body       = parse_body
  payment_id = body['payment_id']
  amount     = body['amount']
  currency   = body['currency']

  log('info', 'processing payment', payment_id: payment_id)

  begin
    resp = Faraday.new(url: GATEWAY_STUB_URL) { |f|
      f.request :json
      f.options.timeout = 10
      f.options.open_timeout = 5
    }.post(
      '/charge',
      { payment_id: payment_id, amount: amount, currency: currency }.to_json,
      'Content-Type' => 'application/json'
    )
    result = JSON.parse(resp.body)
    log('info', 'gateway response', payment_id: payment_id, status: result['status'])
    result.to_json
  rescue => e
    log('error', 'gateway stub error', payment_id: payment_id, error: e.message)
    halt 502, { error: 'gateway unavailable' }.to_json
  end
end
