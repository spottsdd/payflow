package main

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/twmb/franz-go/pkg/kgo"
)

const serviceName = "notification-service"

func logEntry(level, service, message string, fields map[string]any) {
	entry := map[string]any{
		"timestamp": time.Now().UTC().Format(time.RFC3339Nano),
		"level":     level,
		"service":   service,
		"message":   message,
		"trace_id":  "",
		"span_id":   "",
	}
	for k, v := range fields {
		entry[k] = v
	}
	b, _ := json.Marshal(entry)
	fmt.Println(string(b))
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

var topicToType = map[string]string{
	"payment.completed": "PAYMENT_COMPLETED",
	"payment.failed":    "PAYMENT_FAILED",
}

func startConsumer(db *pgx.Conn, kc *kgo.Client) {
	for {
		fetches := kc.PollFetches(context.Background())
		if errs := fetches.Errors(); len(errs) > 0 {
			for _, fe := range errs {
				logEntry("error", serviceName, "kafka fetch error", map[string]any{"error": fe.Err.Error()})
			}
			continue
		}

		fetches.EachRecord(func(record *kgo.Record) {
			notifType, ok := topicToType[record.Topic]
			if !ok {
				return
			}

			var payload map[string]any
			if err := json.Unmarshal(record.Value, &payload); err != nil {
				logEntry("error", serviceName, "failed to parse kafka message", map[string]any{"error": err.Error()})
				return
			}

			paymentID, _ := payload["payment_id"].(string)
			if paymentID == "" {
				logEntry("warn", serviceName, "missing payment_id in kafka message", nil)
				return
			}

			_, err := db.Exec(context.Background(),
				`INSERT INTO notifications (id, payment_id, type, payload, created_at)
				 VALUES ($1, $2, $3, $4, NOW())`,
				uuid.New().String(), paymentID, notifType, record.Value,
			)
			if err != nil {
				logEntry("error", serviceName, "notification insert failed", map[string]any{
					"payment_id": paymentID,
					"type":       notifType,
					"error":      err.Error(),
				})
				return
			}

			logEntry("info", serviceName, "notification stored", map[string]any{
				"payment_id": paymentID,
				"type":       notifType,
			})
		})
	}
}

func main() {
	databaseURL := getEnv("DATABASE_URL", "postgresql://payflow:payflow@postgres:5432/payflow")
	kafkaBrokers := getEnv("KAFKA_BROKERS", "kafka:9092")
	port := getEnv("PORT", "8085")

	db, err := pgx.Connect(context.Background(), databaseURL)
	if err != nil {
		logEntry("error", serviceName, "db connect failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
	defer db.Close(context.Background())

	kc, err := kgo.NewClient(
		kgo.SeedBrokers(kafkaBrokers),
		kgo.ConsumerGroup("notification-service"),
		kgo.ConsumeTopics("payment.completed", "payment.failed"),
	)
	if err != nil {
		logEntry("error", serviceName, "kafka connect failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
	defer kc.Close()

	go startConsumer(db, kc)

	gin.SetMode(gin.ReleaseMode)
	r := gin.New()
	r.Use(gin.Recovery())

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	logEntry("info", serviceName, "starting", map[string]any{"port": port})
	if err := r.Run(":" + port); err != nil {
		logEntry("error", serviceName, "server failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
}
