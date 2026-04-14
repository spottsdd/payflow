using System.Text.Json;
using PaymentProcessor;

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

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
};

var gatewayStubUrl = Environment.GetEnvironmentVariable("GATEWAY_STUB_URL")
    ?? "http://gateway-stub:9999";

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddHttpClient("gateway", c =>
    c.BaseAddress = new Uri(gatewayStubUrl));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/process", async (ProcessRequest req, IHttpClientFactory factory) =>
{
    Log("INFO", "payment-processor", "processing payment",
        new { payment_id = req.PaymentId, amount = req.Amount });

    var client = factory.CreateClient("gateway");
    var chargeReq = new GatewayChargeRequest(req.PaymentId, req.Amount, req.Currency);
    var resp = await client.PostAsJsonAsync("/charge", chargeReq, jsonOpts);
    resp.EnsureSuccessStatusCode();

    var result = await resp.Content.ReadFromJsonAsync<GatewayChargeResponse>(jsonOpts);
    return Results.Ok(new ProcessResponse(result!.Status, result.TransactionId));
});

app.Run();
