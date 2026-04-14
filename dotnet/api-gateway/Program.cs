using System.Text.Json;
using ApiGateway;

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

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var orchestratorUrl = Environment.GetEnvironmentVariable("ORCHESTRATOR_URL")
    ?? "http://payment-orchestrator:8081";

builder.Services.AddHttpClient("orchestrator", c =>
    c.BaseAddress = new Uri(orchestratorUrl));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/payments", async (HttpContext ctx, IHttpClientFactory factory) =>
{
    PaymentRequest? req;
    try
    {
        req = await ctx.Request.ReadFromJsonAsync<PaymentRequest>(jsonOpts);
    }
    catch
    {
        return Results.BadRequest(new { error = "invalid request body" });
    }

    if (req is null)
        return Results.BadRequest(new { error = "request body required" });
    if (req.FromAccountId is null)
        return Results.BadRequest(new { error = "from_account_id is required" });
    if (req.ToAccountId is null)
        return Results.BadRequest(new { error = "to_account_id is required" });
    if (req.Amount is null || req.Amount <= 0)
        return Results.BadRequest(new { error = "amount must be greater than 0" });
    if (string.IsNullOrEmpty(req.Currency) || req.Currency.Length != 3)
        return Results.BadRequest(new { error = "currency must be 3 characters" });
    if (req.FromAccountId == req.ToAccountId)
        return Results.BadRequest(new { error = "from_account_id and to_account_id must be different" });

    Log("INFO", "api-gateway", "forwarding payment request",
        new { amount = req.Amount, currency = req.Currency });

    var client = factory.CreateClient("orchestrator");
    var resp = await client.PostAsJsonAsync("/payments", req, jsonOpts);
    var body = await resp.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json", null, (int)resp.StatusCode);
});

app.MapGet("/payments/{id}", async (string id, IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("orchestrator");
    var resp = await client.GetAsync($"/payments/{id}");
    var body = await resp.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json", null, (int)resp.StatusCode);
});

app.Run();
