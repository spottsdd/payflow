using System.Text.Json;
using FraudDetection;

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

var fraudLatencyMs = int.Parse(
    Environment.GetEnvironmentVariable("FRAUD_LATENCY_MS") ?? "0");

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/fraud/check", async (FraudCheckRequest req) =>
{
    if (fraudLatencyMs > 0)
        await Task.Delay(fraudLatencyMs);

    Log("INFO", "fraud-detection", "checking fraud",
        new { payment_id = req.PaymentId, amount = req.Amount });

    if (req.Amount > 10000)
        return Results.Ok(new FraudCheckResponse(0.95, "DENY"));
    if (req.Amount > 2000)
        return Results.Ok(new FraudCheckResponse(0.65, "FLAG"));

    return Results.Ok(new FraudCheckResponse(0.10, "APPROVE"));
});

app.Run();
