using System.Text.Json;
using Confluent.Kafka;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.JsonWriterOptions = new() { Indented = false };
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

class KafkaConsumerService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<KafkaConsumerService> _logger;

    public KafkaConsumerService(IConfiguration config, ILogger<KafkaConsumerService> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var brokers = _config["KAFKA_BROKERS"] ?? "kafka:9092";
        var connStr = _config["DATABASE_URL"] ?? "Host=postgres;Port=5432;Database=payflow;Username=payflow;Password=payflow";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = brokers,
            GroupId = "notification-service",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        consumer.Subscribe(["payment.completed", "payment.failed"]);

        _logger.LogInformation("Kafka consumer started, subscribed to payment topics");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result?.Message is null) continue;

                var paymentId = ExtractPaymentId(result.Message.Value);

                await using var conn = new NpgsqlConnection(connStr);
                await conn.ExecuteAsync(
                    "INSERT INTO notifications (id, payment_id, type, payload, created_at) " +
                    "VALUES (@id, @paymentId, @type, @payload::jsonb, @createdAt)",
                    new
                    {
                        id = Guid.NewGuid(),
                        paymentId,
                        type = result.Topic,
                        payload = result.Message.Value,
                        createdAt = DateTime.UtcNow
                    });

                consumer.Commit(result);
                _logger.LogInformation("Notification stored for {Topic} payment {PaymentId}",
                    result.Topic, paymentId);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error");
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }

    private static Guid ExtractPaymentId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("payment_id", out var prop))
                return Guid.Parse(prop.GetString() ?? Guid.Empty.ToString());
        }
        catch { }
        return Guid.Empty;
    }
}
