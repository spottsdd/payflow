package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"strconv"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/twmb/franz-go/pkg/kgo"
)

const serviceName = "payment-orchestrator"

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

func getEnvInt(key string, fallback int) int {
	if v := os.Getenv(key); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			return n
		}
	}
	return fallback
}

type paymentRequest struct {
	FromAccountID string  `json:"from_account_id"`
	ToAccountID   string  `json:"to_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
}

type fraudCheckRequest struct {
	PaymentID     string  `json:"payment_id"`
	FromAccountID string  `json:"from_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
}

type fraudCheckResponse struct {
	RiskScore float64 `json:"risk_score"`
	Decision  string  `json:"decision"`
}

type processRequest struct {
	PaymentID     string  `json:"payment_id"`
	FromAccountID string  `json:"from_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
}

type processResponse struct {
	Status        string `json:"status"`
	TransactionID string `json:"transaction_id"`
}

type settleRequest struct {
	PaymentID     string  `json:"payment_id"`
	FromAccountID string  `json:"from_account_id"`
	ToAccountID   string  `json:"to_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
	TransactionID string  `json:"transaction_id"`
}

func postJSON(client *http.Client, url string, payload any) (*http.Response, error) {
	b, err := json.Marshal(payload)
	if err != nil {
		return nil, err
	}
	req, err := http.NewRequest(http.MethodPost, url, bytes.NewReader(b))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")
	return client.Do(req)
}

func produceEvent(kc *kgo.Client, topic, paymentID, status string) {
	payload, _ := json.Marshal(map[string]any{
		"payment_id": paymentID,
		"status":     status,
		"timestamp":  time.Now().UTC().Format(time.RFC3339Nano),
	})
	record := &kgo.Record{
		Topic: topic,
		Key:   []byte(paymentID),
		Value: payload,
	}
	if err := kc.ProduceSync(context.Background(), record).FirstErr(); err != nil {
		logEntry("error", serviceName, "kafka produce failed", map[string]any{"topic": topic, "error": err.Error()})
	}
}

func updatePaymentStatus(db *pgx.Conn, paymentID, status string) {
	_, err := db.Exec(context.Background(),
		"UPDATE payments SET status=$1, updated_at=NOW() WHERE id=$2",
		status, paymentID,
	)
	if err != nil {
		logEntry("error", serviceName, "db update failed", map[string]any{"payment_id": paymentID, "status": status, "error": err.Error()})
	}
}

func main() {
	databaseURL := getEnv("DATABASE_URL", "postgresql://payflow:payflow@postgres:5432/payflow")
	kafkaBrokers := getEnv("KAFKA_BROKERS", "kafka:9092")
	fraudURL := getEnv("FRAUD_SERVICE_URL", "http://fraud-detection:8082")
	processorURL := getEnv("PROCESSOR_SERVICE_URL", "http://payment-processor:8083")
	settlementURL := getEnv("SETTLEMENT_SERVICE_URL", "http://settlement-service:8084")
	port := getEnv("PORT", "8081")
	fraudTimeoutMS := getEnvInt("FRAUD_TIMEOUT_MS", 5000)
	processorTimeoutMS := getEnvInt("PROCESSOR_TIMEOUT_MS", 10000)
	settlementTimeoutMS := getEnvInt("SETTLEMENT_TIMEOUT_MS", 5000)

	db, err := pgx.Connect(context.Background(), databaseURL)
	if err != nil {
		logEntry("error", serviceName, "db connect failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
	defer db.Close(context.Background())

	kc, err := kgo.NewClient(kgo.SeedBrokers(kafkaBrokers))
	if err != nil {
		logEntry("error", serviceName, "kafka connect failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
	defer kc.Close()

	fraudClient := &http.Client{Timeout: time.Duration(fraudTimeoutMS) * time.Millisecond}
	processorClient := &http.Client{Timeout: time.Duration(processorTimeoutMS) * time.Millisecond}
	settlementClient := &http.Client{Timeout: time.Duration(settlementTimeoutMS) * time.Millisecond}

	gin.SetMode(gin.ReleaseMode)
	r := gin.New()
	r.Use(gin.Recovery())

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	r.GET("/payments/:id", func(c *gin.Context) {
		id := c.Param("id")
		row := db.QueryRow(context.Background(),
			"SELECT id, status, amount, currency, created_at FROM payments WHERE id=$1", id,
		)
		var (
			paymentID string
			status    string
			amount    float64
			currency  string
			createdAt time.Time
		)
		if err := row.Scan(&paymentID, &status, &amount, &currency, &createdAt); err != nil {
			if err == pgx.ErrNoRows {
				c.JSON(http.StatusNotFound, gin.H{"error": "not found"})
			} else {
				logEntry("error", serviceName, "db query failed", map[string]any{"error": err.Error()})
				c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			}
			return
		}
		c.JSON(http.StatusOK, gin.H{
			"payment_id": paymentID,
			"status":     status,
			"amount":     amount,
			"currency":   currency,
			"created_at": createdAt.UTC().Format(time.RFC3339Nano),
		})
	})

	r.POST("/payments", func(c *gin.Context) {
		var req paymentRequest
		if err := c.ShouldBindJSON(&req); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}

		paymentID := uuid.New().String()
		_, err := db.Exec(context.Background(),
			`INSERT INTO payments (id, from_account_id, to_account_id, amount, currency, status, created_at, updated_at)
			 VALUES ($1, $2, $3, $4, $5, 'PENDING', NOW(), NOW())`,
			paymentID, req.FromAccountID, req.ToAccountID, req.Amount, req.Currency,
		)
		if err != nil {
			logEntry("error", serviceName, "db insert failed", map[string]any{"error": err.Error()})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			return
		}

		logEntry("info", serviceName, "payment created", map[string]any{"payment_id": paymentID})

		// Fraud check
		fraudResp, err := postJSON(fraudClient, fraudURL+"/fraud/check", fraudCheckRequest{
			PaymentID:     paymentID,
			FromAccountID: req.FromAccountID,
			Amount:        req.Amount,
			Currency:      req.Currency,
		})
		if err != nil {
			logEntry("error", serviceName, "fraud check failed", map[string]any{"payment_id": paymentID, "error": err.Error()})
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusInternalServerError, gin.H{"error": "fraud service unavailable"})
			return
		}
		defer fraudResp.Body.Close()
		fraudBody, _ := io.ReadAll(fraudResp.Body)

		var fraudResult fraudCheckResponse
		if err := json.Unmarshal(fraudBody, &fraudResult); err != nil {
			logEntry("error", serviceName, "fraud response parse failed", map[string]any{"payment_id": paymentID, "error": err.Error()})
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusInternalServerError, gin.H{"error": "fraud service error"})
			return
		}

		logEntry("info", serviceName, "fraud check complete", map[string]any{
			"payment_id": paymentID,
			"decision":   fraudResult.Decision,
			"risk_score": fraudResult.RiskScore,
		})

		if fraudResult.Decision == "DENY" {
			updatePaymentStatus(db, paymentID, "DECLINED")
			produceEvent(kc, "payment.failed", paymentID, "DECLINED")
			c.JSON(http.StatusOK, gin.H{"payment_id": paymentID, "status": "DECLINED", "transaction_id": ""})
			return
		}

		// Payment processing
		procResp, err := postJSON(processorClient, processorURL+"/process", processRequest{
			PaymentID:     paymentID,
			FromAccountID: req.FromAccountID,
			Amount:        req.Amount,
			Currency:      req.Currency,
		})
		if err != nil {
			logEntry("error", serviceName, "processor call failed", map[string]any{"payment_id": paymentID, "error": err.Error()})
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusInternalServerError, gin.H{"error": "processor unavailable"})
			return
		}
		defer procResp.Body.Close()
		procBody, _ := io.ReadAll(procResp.Body)

		var procResult processResponse
		if err := json.Unmarshal(procBody, &procResult); err != nil {
			logEntry("error", serviceName, "processor response parse failed", map[string]any{"payment_id": paymentID, "error": err.Error()})
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusInternalServerError, gin.H{"error": "processor error"})
			return
		}

		logEntry("info", serviceName, "processor complete", map[string]any{
			"payment_id":     paymentID,
			"status":         procResult.Status,
			"transaction_id": procResult.TransactionID,
		})

		if procResult.Status == "DECLINED" {
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusOK, gin.H{"payment_id": paymentID, "status": "FAILED", "transaction_id": procResult.TransactionID})
			return
		}

		// Settlement
		settleResp, err := postJSON(settlementClient, settlementURL+"/settle", settleRequest{
			PaymentID:     paymentID,
			FromAccountID: req.FromAccountID,
			ToAccountID:   req.ToAccountID,
			Amount:        req.Amount,
			Currency:      req.Currency,
			TransactionID: procResult.TransactionID,
		})
		if err != nil {
			logEntry("error", serviceName, "settlement call failed", map[string]any{"payment_id": paymentID, "error": err.Error()})
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusInternalServerError, gin.H{"error": "settlement unavailable"})
			return
		}
		defer settleResp.Body.Close()

		if settleResp.StatusCode == http.StatusPaymentRequired {
			logEntry("warn", serviceName, "insufficient funds", map[string]any{"payment_id": paymentID})
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusOK, gin.H{"payment_id": paymentID, "status": "FAILED", "transaction_id": procResult.TransactionID})
			return
		}
		if settleResp.StatusCode != http.StatusOK {
			logEntry("error", serviceName, "settlement error", map[string]any{"payment_id": paymentID, "status_code": settleResp.StatusCode})
			updatePaymentStatus(db, paymentID, "FAILED")
			produceEvent(kc, "payment.failed", paymentID, "FAILED")
			c.JSON(http.StatusInternalServerError, gin.H{"error": "settlement error"})
			return
		}

		updatePaymentStatus(db, paymentID, "COMPLETED")
		produceEvent(kc, "payment.completed", paymentID, "COMPLETED")

		logEntry("info", serviceName, "payment completed", map[string]any{"payment_id": paymentID, "transaction_id": procResult.TransactionID})
		c.JSON(http.StatusOK, gin.H{"payment_id": paymentID, "status": "COMPLETED", "transaction_id": procResult.TransactionID})
	})

	logEntry("info", serviceName, "starting", map[string]any{"port": port})
	if err := r.Run(":" + port); err != nil {
		logEntry("error", serviceName, "server failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
}
