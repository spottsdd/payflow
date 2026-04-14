using System.Net.Http.Json;

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

app.MapPost("/process", async (ProcessRequest req, IHttpClientFactory factory, IConfiguration config, ILogger<Program> logger) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["service"] = "payment-processor",
        ["trace_id"] = "",
        ["span_id"] = ""
    });

    var gatewayStubUrl = config["GATEWAY_STUB_URL"] ?? "http://gateway-stub:9999";
    var client = factory.CreateClient();

    try
    {
        var response = await client.PostAsJsonAsync($"{gatewayStubUrl}/charge",
            new { payment_id = req.payment_id, amount = req.amount, currency = req.currency });

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gateway returned {StatusCode} for payment {PaymentId}",
                (int)response.StatusCode, req.payment_id);
            return Results.Ok(new { status = "DECLINED", transaction_id = "" });
        }

        var result = await response.Content.ReadFromJsonAsync<GatewayResult>();
        logger.LogInformation("Gateway response for {PaymentId}: {Status}", req.payment_id, result?.status);
        return Results.Ok(new { status = result?.status ?? "DECLINED", transaction_id = result?.transaction_id ?? "" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Gateway charge failed for payment {PaymentId}", req.payment_id);
        return Results.Ok(new { status = "DECLINED", transaction_id = "" });
    }
});

app.Run();

record ProcessRequest(string payment_id, decimal amount, string currency);
record GatewayResult(string status, string transaction_id);
