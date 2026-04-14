using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.JsonWriterOptions = new() { Indented = false };
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/settle", async (SettleRequest req, IConfiguration config, ILogger<Program> logger) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["service"] = "settlement-service",
        ["trace_id"] = "",
        ["span_id"] = ""
    });

    var connStr = config["DATABASE_URL"] ?? "Host=postgres;Port=5432;Database=payflow;Username=payflow;Password=payflow";

    await using var conn = new NpgsqlConnection(connStr);
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        var fromId = Guid.Parse(req.from_account_id);
        var toId = Guid.Parse(req.to_account_id);
        var paymentId = Guid.Parse(req.payment_id);

        var balance = await conn.QueryFirstOrDefaultAsync<decimal>(
            "SELECT balance FROM accounts WHERE id = @id FOR UPDATE",
            new { id = fromId }, tx);

        if (balance < req.amount)
        {
            await tx.RollbackAsync();
            logger.LogWarning("Insufficient funds for payment {PaymentId}: balance={Balance} amount={Amount}",
                req.payment_id, balance, req.amount);
            return Results.Json(new { error = "insufficient funds" }, statusCode: 402);
        }

        await conn.ExecuteAsync(
            "UPDATE accounts SET balance = balance - @amount WHERE id = @id",
            new { id = fromId, amount = req.amount }, tx);

        await conn.ExecuteAsync(
            "UPDATE accounts SET balance = balance + @amount WHERE id = @id",
            new { id = toId, amount = req.amount }, tx);

        var settlementId = Guid.NewGuid();
        var settledAt = DateTime.UtcNow;

        await conn.ExecuteAsync(
            "INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at) " +
            "VALUES (@id, @paymentId, @from, @to, @amount, @settledAt)",
            new { id = settlementId, paymentId, from = fromId, to = toId, amount = req.amount, settledAt }, tx);

        await tx.CommitAsync();

        logger.LogInformation("Settlement {SettlementId} completed for payment {PaymentId}",
            settlementId, req.payment_id);
        return Results.Ok(new { settlement_id = settlementId, settled_at = settledAt });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        logger.LogError(ex, "Settlement failed for payment {PaymentId}", req.payment_id);
        return Results.Problem("settlement failed", statusCode: 500);
    }
});

app.Run();

record SettleRequest(string payment_id, string from_account_id, string to_account_id, decimal amount);
