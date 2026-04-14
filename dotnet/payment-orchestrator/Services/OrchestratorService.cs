using System.Net;
using System.Text.Json;
using Confluent.Kafka;
using Dapper;
using Npgsql;

namespace PaymentOrchestrator;

public class OrchestratorService(
    IHttpClientFactory httpClientFactory,
    IProducer<Null, string> producer,
    OrchestratorConfig config)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IResult> ProcessAsync(PaymentRequest req)
    {
        var paymentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(config.DatabaseUrl);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO payments (id, from_account_id, to_account_id, amount, currency, status, created_at, updated_at)
            VALUES (@id, @from, @to, @amount, @currency, 'PENDING', @now, @now)
            """,
            new { id = paymentId, from = req.FromAccountId, to = req.ToAccountId,
                  amount = req.Amount, currency = req.Currency, now });

        // Fraud check
        FraudCheckResponse? fraud;
        try
        {
            using var cts = new CancellationTokenSource(config.FraudTimeoutMs);
            var client = httpClientFactory.CreateClient("fraud");
            var resp = await client.PostAsJsonAsync("/fraud/check",
                new FraudCheckRequest(paymentId, req.Amount, req.Currency), JsonOpts, cts.Token);
            resp.EnsureSuccessStatusCode();
            fraud = await resp.Content.ReadFromJsonAsync<FraudCheckResponse>(JsonOpts, cts.Token);
        }
        catch (Exception ex)
        {
            Log("ERROR", "payment-orchestrator", $"fraud check failed: {ex.Message}",
                new { payment_id = paymentId });
            await UpdateStatusAsync(conn, paymentId, "FAILED");
            await ProduceAsync("payment.failed", paymentId, "FAILED", "");
            return Results.StatusCode(500);
        }

        if (fraud?.Decision == "DENY")
        {
            await UpdateStatusAsync(conn, paymentId, "DECLINED");
            await ProduceAsync("payment.failed", paymentId, "DECLINED", "");
            return Results.Ok(new PaymentResponse(paymentId, "DECLINED", ""));
        }

        // Process payment
        ProcessResponse? processed;
        try
        {
            using var cts = new CancellationTokenSource(config.ProcessorTimeoutMs);
            var client = httpClientFactory.CreateClient("processor");
            var resp = await client.PostAsJsonAsync("/process",
                new ProcessRequest(paymentId, req.Amount, req.Currency), JsonOpts, cts.Token);
            resp.EnsureSuccessStatusCode();
            processed = await resp.Content.ReadFromJsonAsync<ProcessResponse>(JsonOpts, cts.Token);
        }
        catch (Exception ex)
        {
            Log("ERROR", "payment-orchestrator", $"processor failed: {ex.Message}",
                new { payment_id = paymentId });
            await UpdateStatusAsync(conn, paymentId, "FAILED");
            await ProduceAsync("payment.failed", paymentId, "FAILED", "");
            return Results.StatusCode(500);
        }

        if (processed?.Status == "DECLINED")
        {
            await UpdateStatusAsync(conn, paymentId, "FAILED");
            await ProduceAsync("payment.failed", paymentId, "FAILED", processed.TransactionId);
            return Results.Ok(new PaymentResponse(paymentId, "FAILED", processed.TransactionId));
        }

        var transactionId = processed?.TransactionId ?? "";

        // Settle
        try
        {
            using var cts = new CancellationTokenSource(config.SettlementTimeoutMs);
            var client = httpClientFactory.CreateClient("settlement");
            var resp = await client.PostAsJsonAsync("/settle",
                new SettleRequest(paymentId, req.FromAccountId, req.ToAccountId,
                    req.Amount, req.Currency, transactionId),
                JsonOpts, cts.Token);

            if (resp.StatusCode == HttpStatusCode.PaymentRequired)
            {
                await UpdateStatusAsync(conn, paymentId, "FAILED");
                await ProduceAsync("payment.failed", paymentId, "FAILED", transactionId);
                return Results.Ok(new PaymentResponse(paymentId, "FAILED", transactionId));
            }

            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not HttpRequestException { StatusCode: HttpStatusCode.PaymentRequired })
        {
            Log("ERROR", "payment-orchestrator", $"settlement failed: {ex.Message}",
                new { payment_id = paymentId });
            await UpdateStatusAsync(conn, paymentId, "FAILED");
            await ProduceAsync("payment.failed", paymentId, "FAILED", transactionId);
            return Results.StatusCode(500);
        }

        await conn.ExecuteAsync(
            "UPDATE payments SET status = 'COMPLETED', updated_at = NOW() WHERE id = @id",
            new { id = paymentId });

        await ProduceAsync("payment.completed", paymentId, "COMPLETED", transactionId);

        Log("INFO", "payment-orchestrator", "payment completed",
            new { payment_id = paymentId, transaction_id = transactionId });

        return Results.Ok(new PaymentResponse(paymentId, "COMPLETED", transactionId));
    }

    public async Task<IResult> GetPaymentAsync(string id)
    {
        if (!Guid.TryParse(id, out var paymentId))
            return Results.NotFound(new { error = "not found" });

        await using var conn = new NpgsqlConnection(config.DatabaseUrl);
        var payment = await conn.QuerySingleOrDefaultAsync<PaymentRecord>(
            "SELECT * FROM payments WHERE id = @id",
            new { id = paymentId });

        if (payment is null)
            return Results.NotFound(new { error = "not found" });

        return Results.Ok(new
        {
            payment_id = payment.Id,
            status = payment.Status,
            amount = payment.Amount,
            currency = payment.Currency,
            created_at = payment.CreatedAt,
        });
    }

    private static async Task UpdateStatusAsync(NpgsqlConnection conn, Guid paymentId, string status)
    {
        await conn.ExecuteAsync(
            "UPDATE payments SET status = @status, updated_at = NOW() WHERE id = @id",
            new { status, id = paymentId });
    }

    private async Task ProduceAsync(string topic, Guid paymentId, string status, string transactionId)
    {
        var message = JsonSerializer.Serialize(new
        {
            payment_id = paymentId,
            status,
            transaction_id = transactionId,
        });
        try
        {
            await producer.ProduceAsync(topic, new Message<Null, string> { Value = message });
        }
        catch (Exception ex)
        {
            Log("ERROR", "payment-orchestrator", $"kafka produce failed: {ex.Message}",
                new { topic, payment_id = paymentId });
        }
    }

    private static void Log(string level, string service, string message, object? extra = null)
    {
        var entry = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["level"] = level,
            ["service"] = service,
            ["message"] = message,
            ["trace_id"] = "",
            ["span_id"] = "",
        };
        if (extra != null)
            foreach (var prop in extra.GetType().GetProperties())
                entry[prop.Name] = prop.GetValue(extra);
        Console.WriteLine(JsonSerializer.Serialize(entry));
    }
}

public record OrchestratorConfig(
    string DatabaseUrl,
    int FraudTimeoutMs,
    int ProcessorTimeoutMs,
    int SettlementTimeoutMs
);
