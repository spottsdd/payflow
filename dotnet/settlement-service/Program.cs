using System.Text.Json;
using Dapper;
using Npgsql;
using SettlementService;

static void Log(string level, string service, string message, object? extra = null)
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

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=postgres;Database=payflow;Username=payflow;Password=payflow";

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/settle", async (SettleRequest req) =>
{
    Log("INFO", "settlement-service", "settling payment",
        new { payment_id = req.PaymentId, amount = req.Amount });

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        var balance = await conn.QuerySingleOrDefaultAsync<decimal>(
            "SELECT balance FROM accounts WHERE id = @id FOR UPDATE",
            new { id = req.FromAccountId }, tx);

        if (balance < req.Amount)
        {
            await tx.RollbackAsync();
            Log("WARN", "settlement-service", "insufficient funds",
                new { payment_id = req.PaymentId, balance, amount = req.Amount });
            return Results.Json(new { error = "insufficient funds" }, statusCode: 402);
        }

        await conn.ExecuteAsync(
            "UPDATE accounts SET balance = balance - @amount WHERE id = @id",
            new { amount = req.Amount, id = req.FromAccountId }, tx);

        await conn.ExecuteAsync(
            "UPDATE accounts SET balance = balance + @amount WHERE id = @id",
            new { amount = req.Amount, id = req.ToAccountId }, tx);

        var settlementId = Guid.NewGuid();
        var settledAt = await conn.QuerySingleAsync<DateTime>(
            """
            INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at)
            VALUES (@id, @paymentId, @fromId, @toId, @amount, NOW())
            RETURNING settled_at
            """,
            new
            {
                id = settlementId,
                paymentId = req.PaymentId,
                fromId = req.FromAccountId,
                toId = req.ToAccountId,
                amount = req.Amount,
            }, tx);

        await tx.CommitAsync();

        Log("INFO", "settlement-service", "settlement completed",
            new { settlement_id = settlementId, payment_id = req.PaymentId });

        return Results.Ok(new SettleResponse(settlementId, settledAt));
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
});

app.Run();
