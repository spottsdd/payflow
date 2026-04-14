var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.JsonWriterOptions = new() { Indented = false };
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/fraud/check", async (FraudRequest req, ILogger<Program> logger) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["service"] = "fraud-detection",
        ["trace_id"] = "",
        ["span_id"] = ""
    });

    var latencyMs = int.Parse(Environment.GetEnvironmentVariable("FRAUD_LATENCY_MS") ?? "0");
    if (latencyMs > 0)
        await Task.Delay(latencyMs);

    var (riskScore, decision) = req.amount switch
    {
        > 10000m => (0.9, "DENY"),
        > 5000m  => (0.5, "REVIEW"),
        _        => (0.1, "APPROVE")
    };

    logger.LogInformation("Fraud check {PaymentId}: score={RiskScore} decision={Decision}",
        req.payment_id, riskScore, decision);

    return Results.Ok(new { risk_score = riskScore, decision });
});

app.Run();

record FraudRequest(string payment_id, decimal amount, string from_account_id);
