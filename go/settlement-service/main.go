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
)

const serviceName = "settlement-service"

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

type settleRequest struct {
	PaymentID     string  `json:"payment_id"`
	FromAccountID string  `json:"from_account_id"`
	ToAccountID   string  `json:"to_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
	TransactionID string  `json:"transaction_id"`
}

func main() {
	databaseURL := getEnv("DATABASE_URL", "postgresql://payflow:payflow@postgres:5432/payflow")
	port := getEnv("PORT", "8084")

	db, err := pgx.Connect(context.Background(), databaseURL)
	if err != nil {
		logEntry("error", serviceName, "db connect failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
	defer db.Close(context.Background())

	gin.SetMode(gin.ReleaseMode)
	r := gin.New()
	r.Use(gin.Recovery())

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	r.POST("/settle", func(c *gin.Context) {
		var req settleRequest
		if err := c.ShouldBindJSON(&req); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}

		tx, err := db.BeginTx(context.Background(), pgx.TxOptions{})
		if err != nil {
			logEntry("error", serviceName, "begin tx failed", map[string]any{"error": err.Error()})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			return
		}

		var balance float64
		err = tx.QueryRow(context.Background(),
			"SELECT balance FROM accounts WHERE id=$1 FOR UPDATE",
			req.FromAccountID,
		).Scan(&balance)
		if err != nil {
			tx.Rollback(context.Background())
			logEntry("error", serviceName, "balance query failed", map[string]any{
				"payment_id": req.PaymentID,
				"error":      err.Error(),
			})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "account not found"})
			return
		}

		if balance < req.Amount {
			tx.Rollback(context.Background())
			logEntry("warn", serviceName, "insufficient funds", map[string]any{
				"payment_id": req.PaymentID,
				"balance":    balance,
				"amount":     req.Amount,
			})
			c.JSON(http.StatusPaymentRequired, gin.H{"error": "insufficient funds"})
			return
		}

		if _, err = tx.Exec(context.Background(),
			"UPDATE accounts SET balance=balance-$1 WHERE id=$2",
			req.Amount, req.FromAccountID,
		); err != nil {
			tx.Rollback(context.Background())
			logEntry("error", serviceName, "debit failed", map[string]any{"error": err.Error()})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			return
		}

		if _, err = tx.Exec(context.Background(),
			"UPDATE accounts SET balance=balance+$1 WHERE id=$2",
			req.Amount, req.ToAccountID,
		); err != nil {
			tx.Rollback(context.Background())
			logEntry("error", serviceName, "credit failed", map[string]any{"error": err.Error()})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			return
		}

		settlementID := uuid.New().String()
		var settledAt time.Time
		err = tx.QueryRow(context.Background(),
			`INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at)
			 VALUES ($1, $2, $3, $4, $5, NOW()) RETURNING id, settled_at`,
			settlementID, req.PaymentID, req.FromAccountID, req.ToAccountID, req.Amount,
		).Scan(&settlementID, &settledAt)
		if err != nil {
			tx.Rollback(context.Background())
			logEntry("error", serviceName, "settlement insert failed", map[string]any{"error": err.Error()})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			return
		}

		if err = tx.Commit(context.Background()); err != nil {
			logEntry("error", serviceName, "commit failed", map[string]any{"error": err.Error()})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			return
		}

		logEntry("info", serviceName, "settlement complete", map[string]any{
			"payment_id":    req.PaymentID,
			"settlement_id": settlementID,
			"amount":        req.Amount,
		})

		c.JSON(http.StatusOK, gin.H{
			"settlement_id": settlementID,
			"settled_at":    settledAt.UTC().Format(time.RFC3339Nano),
		})
	})

	logEntry("info", serviceName, "starting", map[string]any{"port": port})
	if err := r.Run(":" + port); err != nil {
		logEntry("error", serviceName, "server failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
}
