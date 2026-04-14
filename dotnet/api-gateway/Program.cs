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

app.MapPost("/payments", async (PaymentRequest req, IHttpClientFactory factory, IConfiguration config, ILogger<Program> logger) =>
{
    if (string.IsNullOrEmpty(req.from_account_id) ||
        string.IsNullOrEmpty(req.to_account_id) ||
        string.IsNullOrEmpty(req.currency) ||
        req.amount <= 0 ||
        req.currency.Length != 3 ||
        req.from_account_id == req.to_account_id)
    {
        return Results.BadRequest(new { error = "invalid payment request" });
    }

    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["service"] = "api-gateway",
        ["trace_id"] = "",
        ["span_id"] = ""
    });

    var orchestratorUrl = config["ORCHESTRATOR_URL"] ?? "http://payment-orchestrator:8081";
    var client = factory.CreateClient();

    try
    {
        var response = await client.PostAsJsonAsync($"{orchestratorUrl}/payments", req);
        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to forward payment to orchestrator");
        return Results.Problem("service unavailable", statusCode: 503);
    }
});

app.MapGet("/payments/{id}", async (string id, IHttpClientFactory factory, IConfiguration config, ILogger<Program> logger) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["service"] = "api-gateway",
        ["trace_id"] = "",
        ["span_id"] = ""
    });

    var orchestratorUrl = config["ORCHESTRATOR_URL"] ?? "http://payment-orchestrator:8081";
    var client = factory.CreateClient();

    try
    {
        var response = await client.GetAsync($"{orchestratorUrl}/payments/{id}");
        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to fetch payment {PaymentId}", id);
        return Results.Problem("service unavailable", statusCode: 503);
    }
});

app.Run();

record PaymentRequest(string from_account_id, string to_account_id, decimal amount, string currency);
