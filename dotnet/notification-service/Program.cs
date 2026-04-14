using System.Text.Json;
using NotificationService;

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

var notificationConfig = new NotificationConfig(
    DatabaseUrl: Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? "Host=postgres;Database=payflow;Username=payflow;Password=payflow",
    KafkaBrokers: Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "kafka:9092"
);

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddSingleton(notificationConfig);
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

app.MapGet("/health", () =>
{
    Log("DEBUG", "notification-service", "health check");
    return Results.Ok(new { status = "ok" });
});

app.Run();
