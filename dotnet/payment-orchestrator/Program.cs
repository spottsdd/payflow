using System.Text.Json;
using Confluent.Kafka;
using PaymentOrchestrator;

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

var config = new OrchestratorConfig(
    DatabaseUrl: Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? "Host=postgres;Database=payflow;Username=payflow;Password=payflow",
    FraudTimeoutMs: int.Parse(Environment.GetEnvironmentVariable("FRAUD_TIMEOUT_MS") ?? "5000"),
    ProcessorTimeoutMs: int.Parse(Environment.GetEnvironmentVariable("PROCESSOR_TIMEOUT_MS") ?? "10000"),
    SettlementTimeoutMs: int.Parse(Environment.GetEnvironmentVariable("SETTLEMENT_TIMEOUT_MS") ?? "5000")
);

var kafkaBrokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "kafka:9092";
var producer = new ProducerBuilder<Null, string>(
    new ProducerConfig { BootstrapServers = kafkaBrokers }).Build();

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddSingleton<IProducer<Null, string>>(producer);
builder.Services.AddSingleton(config);
builder.Services.AddScoped<OrchestratorService>();

var fraudUrl = Environment.GetEnvironmentVariable("FRAUD_SERVICE_URL") ?? "http://fraud-detection:8082";
var processorUrl = Environment.GetEnvironmentVariable("PROCESSOR_SERVICE_URL") ?? "http://payment-processor:8083";
var settlementUrl = Environment.GetEnvironmentVariable("SETTLEMENT_SERVICE_URL") ?? "http://settlement-service:8084";

builder.Services.AddHttpClient("fraud", c => c.BaseAddress = new Uri(fraudUrl));
builder.Services.AddHttpClient("processor", c => c.BaseAddress = new Uri(processorUrl));
builder.Services.AddHttpClient("settlement", c => c.BaseAddress = new Uri(settlementUrl));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/payments", async (PaymentRequest req, OrchestratorService svc) =>
{
    Log("INFO", "payment-orchestrator", "received payment request",
        new { from = req.FromAccountId, to = req.ToAccountId, amount = req.Amount });
    return await svc.ProcessAsync(req);
});

app.MapGet("/payments/{id}", async (string id, OrchestratorService svc) =>
    await svc.GetPaymentAsync(id));

app.Lifetime.ApplicationStopping.Register(() => producer.Dispose());

app.Run();
