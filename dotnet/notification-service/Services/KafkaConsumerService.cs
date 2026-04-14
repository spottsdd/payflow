using System.Text.Json;
using Confluent.Kafka;
using Dapper;
using Npgsql;

namespace NotificationService;

public class KafkaConsumerService(NotificationConfig config) : BackgroundService
{
    private static readonly string[] Topics = ["payment.completed", "payment.failed"];

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run consumer loop on a background thread so it doesn't block startup
        return Task.Run(() => RunConsumerLoop(stoppingToken), stoppingToken);
    }

    private void RunConsumerLoop(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = config.KafkaBrokers,
            GroupId = "notification-service",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        consumer.Subscribe(Topics);

        Log("INFO", "notification-service", "kafka consumer started", new { topics = Topics });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value is null) continue;

                    ProcessMessage(result.Topic, result.Message.Value).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log("ERROR", "notification-service", $"consumer error: {ex.Message}");
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessMessage(string topic, string payload)
    {
        Guid paymentId;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            paymentId = doc.RootElement.GetProperty("payment_id").GetGuid();
        }
        catch (Exception ex)
        {
            Log("ERROR", "notification-service", $"failed to parse message: {ex.Message}",
                new { topic, payload });
            return;
        }

        var type = topic == "payment.completed" ? "PAYMENT_COMPLETED" : "PAYMENT_FAILED";

        await using var conn = new NpgsqlConnection(config.DatabaseUrl);
        await conn.ExecuteAsync(
            """
            INSERT INTO notifications (id, payment_id, type, payload, created_at)
            VALUES (@id, @paymentId, @type, @payload::jsonb, NOW())
            """,
            new { id = Guid.NewGuid(), paymentId, type, payload });

        Log("INFO", "notification-service", "notification saved",
            new { type, payment_id = paymentId });
    }

    private static void Log(string level, string service, string message, object? extra = null)
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
}
