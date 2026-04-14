require 'sinatra'
require 'json'
require 'pg'
require 'securerandom'
require 'uri'

SERVICE_NAME = 'settlement-service'.freeze

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

set :port, ENV.fetch('PORT', '8084').to_i
set :bind, '0.0.0.0'

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

get '/health' do
  { status: 'ok' }.to_json
end

post '/settle' do
  body           = parse_body
  payment_id     = body['payment_id']
  from_account   = body['from_account_id']
  to_account     = body['to_account_id']
  amount         = body['amount']
  transaction_id = body['transaction_id']

  log('info', 'settling payment', payment_id: payment_id, amount: amount)

  conn = db
  conn.exec('BEGIN')

  from_result = conn.exec_params(
    'SELECT balance FROM accounts WHERE id=$1 FOR UPDATE',
    [from_account]
  )

  if from_result.ntuples == 0
    conn.exec('ROLLBACK')
    halt 404, { error: 'source account not found' }.to_json
  end

  balance = from_result[0]['balance'].to_f

  if balance < amount.to_f
    conn.exec('ROLLBACK')
    log('warn', 'insufficient funds', payment_id: payment_id, balance: balance, amount: amount)
    halt 402, { error: 'insufficient funds' }.to_json
  end

  conn.exec_params('UPDATE accounts SET balance=balance-$1 WHERE id=$2', [amount, from_account])
  conn.exec_params('UPDATE accounts SET balance=balance+$1 WHERE id=$2', [amount, to_account])

  settlement_id = SecureRandom.uuid
  result = conn.exec_params(
    'INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at) VALUES ($1,$2,$3,$4,$5,NOW()) RETURNING id, settled_at',
    [settlement_id, payment_id, from_account, to_account, amount]
  )

  conn.exec('COMMIT')

  row = result[0]
  log('info', 'settlement complete', payment_id: payment_id, settlement_id: row['id'])
  { settlement_id: row['id'], settled_at: row['settled_at'] }.to_json
end
