using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.JsonWriterOptions = new() { Indented = false };
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/payments", async (PaymentRequest req, IHttpClientFactory factory, IConfiguration config, ILogger<Program> logger) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["service"] = "payment-orchestrator",
        ["trace_id"] = "",
        ["span_id"] = ""
    });

    var connStr = config["DATABASE_URL"] ?? "Host=postgres;Port=5432;Database=payflow;Username=payflow;Password=payflow";
    var fraudUrl = config["FRAUD_SERVICE_URL"] ?? "http://fraud-detection:8082";
    var processorUrl = config["PROCESSOR_SERVICE_URL"] ?? "http://payment-processor:8083";
    var settlementUrl = config["SETTLEMENT_SERVICE_URL"] ?? "http://settlement-service:8084";
    var kafkaBrokers = config["KAFKA_BROKERS"] ?? "kafka:9092";
    var fraudTimeoutMs = int.Parse(config["FRAUD_TIMEOUT_MS"] ?? "5000");
    var processorTimeoutMs = int.Parse(config["PROCESSOR_TIMEOUT_MS"] ?? "10000");
    var settlementTimeoutMs = int.Parse(config["SETTLEMENT_TIMEOUT_MS"] ?? "5000");

    var paymentId = Guid.NewGuid();
    var now = DateTime.UtcNow;
    var client = factory.CreateClient();

    await using var conn = new NpgsqlConnection(connStr);
    await conn.ExecuteAsync(
        "INSERT INTO payments (id, from_account_id, to_account_id, amount, currency, status, created_at, updated_at) " +
        "VALUES (@id, @from, @to, @amount, @currency, 'PENDING', @now, @now)",
        new { id = paymentId, from = Guid.Parse(req.from_account_id), to = Guid.Parse(req.to_account_id), amount = req.amount, currency = req.currency, now });

    logger.LogInformation("Payment {PaymentId} created with status PENDING", paymentId);

    // Fraud check
    try
    {
        using var cts = new CancellationTokenSource(fraudTimeoutMs);
        var fraudResp = await client.PostAsJsonAsync($"{fraudUrl}/fraud/check",
            new { payment_id = paymentId, amount = req.amount, from_account_id = req.from_account_id }, cts.Token);
        var fraudResult = await fraudResp.Content.ReadFromJsonAsync<FraudResult>(cancellationToken: cts.Token);

        if (fraudResult?.decision == "DENY")
        {
            await UpdateStatus(conn, paymentId, "DECLINED");
            await PublishEvent(kafkaBrokers, "payment.failed",
                new { payment_id = paymentId, reason = "FRAUD_DECLINED" }, logger);
            return Results.Ok(new { payment_id = paymentId, status = "DECLINED", transaction_id = "" });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Fraud check error for payment {PaymentId}", paymentId);
        await UpdateStatus(conn, paymentId, "FAILED");
        await PublishEvent(kafkaBrokers, "payment.failed",
            new { payment_id = paymentId, reason = "FRAUD_SERVICE_ERROR" }, logger);
        return Results.Ok(new { payment_id = paymentId, status = "FAILED", transaction_id = "" });
    }

    // Process payment
    var transactionId = "";
    try
    {
        using var cts = new CancellationTokenSource(processorTimeoutMs);
        var processResp = await client.PostAsJsonAsync($"{processorUrl}/process",
            new { payment_id = paymentId, amount = req.amount, currency = req.currency }, cts.Token);
        var processResult = await processResp.Content.ReadFromJsonAsync<ProcessResult>(cancellationToken: cts.Token);
        transactionId = processResult?.transaction_id ?? "";

        if (processResult?.status == "DECLINED")
        {
            await UpdateStatus(conn, paymentId, "FAILED");
            await PublishEvent(kafkaBrokers, "payment.failed",
                new { payment_id = paymentId, reason = "PROCESSOR_DECLINED" }, logger);
            return Results.Ok(new { payment_id = paymentId, status = "FAILED", transaction_id = transactionId });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Processor error for payment {PaymentId}", paymentId);
        await UpdateStatus(conn, paymentId, "FAILED");
        await PublishEvent(kafkaBrokers, "payment.failed",
            new { payment_id = paymentId, reason = "PROCESSOR_ERROR" }, logger);
        return Results.Ok(new { payment_id = paymentId, status = "FAILED", transaction_id = transactionId });
    }

    // Settlement
    try
    {
        using var cts = new CancellationTokenSource(settlementTimeoutMs);
        var settleResp = await client.PostAsJsonAsync($"{settlementUrl}/settle",
            new { payment_id = paymentId, from_account_id = req.from_account_id, to_account_id = req.to_account_id, amount = req.amount },
            cts.Token);

        if ((int)settleResp.StatusCode == 402)
        {
            await UpdateStatus(conn, paymentId, "FAILED");
            await PublishEvent(kafkaBrokers, "payment.failed",
                new { payment_id = paymentId, reason = "INSUFFICIENT_FUNDS" }, logger);
            return Results.Ok(new { payment_id = paymentId, status = "FAILED", transaction_id = transactionId });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Settlement error for payment {PaymentId}", paymentId);
        await UpdateStatus(conn, paymentId, "FAILED");
        await PublishEvent(kafkaBrokers, "payment.failed",
            new { payment_id = paymentId, reason = "SETTLEMENT_ERROR" }, logger);
        return Results.Ok(new { payment_id = paymentId, status = "FAILED", transaction_id = transactionId });
    }

    await UpdateStatus(conn, paymentId, "COMPLETED");
    await PublishEvent(kafkaBrokers, "payment.completed",
        new { payment_id = paymentId, amount = req.amount, from_account_id = req.from_account_id }, logger);

    logger.LogInformation("Payment {PaymentId} completed", paymentId);
    return Results.Ok(new { payment_id = paymentId, status = "COMPLETED", transaction_id = transactionId });
});

app.MapGet("/payments/{id}", async (string id, IConfiguration config) =>
{
    var connStr = config["DATABASE_URL"] ?? "Host=postgres;Port=5432;Database=payflow;Username=payflow;Password=payflow";
    if (!Guid.TryParse(id, out var paymentId))
        return Results.BadRequest(new { error = "invalid id" });

    await using var conn = new NpgsqlConnection(connStr);
    var payment = await conn.QueryFirstOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId });
    return payment is null ? Results.NotFound() : Results.Ok(payment);
});

app.Run();

static async Task UpdateStatus(NpgsqlConnection conn, Guid paymentId, string status)
{
    await conn.ExecuteAsync(
        "UPDATE payments SET status = @status, updated_at = @now WHERE id = @id",
        new { id = paymentId, status, now = DateTime.UtcNow });
}

static async Task PublishEvent(string brokers, string topic, object payload, ILogger logger)
{
    var config = new ProducerConfig { BootstrapServers = brokers };
    try
    {
        using var producer = new ProducerBuilder<Null, string>(config).Build();
        await producer.ProduceAsync(topic, new Message<Null, string>
        {
            Value = JsonSerializer.Serialize(payload)
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to publish to {Topic}", topic);
    }
}

record PaymentRequest(string from_account_id, string to_account_id, decimal amount, string currency);
record FraudResult(double risk_score, string decision);
record ProcessResult(string status, string transaction_id);
