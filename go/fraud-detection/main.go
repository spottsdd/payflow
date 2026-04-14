package main

import (
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"strconv"
	"time"

	"github.com/gin-gonic/gin"
)

const serviceName = "fraud-detection"

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

type fraudCheckRequest struct {
	PaymentID     string  `json:"payment_id"`
	FromAccountID string  `json:"from_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
}

func main() {
	port := getEnv("PORT", "8082")
	fraudLatencyMS := getEnvInt("FRAUD_LATENCY_MS", 0)

	gin.SetMode(gin.ReleaseMode)
	r := gin.New()
	r.Use(gin.Recovery())

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	r.POST("/fraud/check", func(c *gin.Context) {
		var req fraudCheckRequest
		if err := c.ShouldBindJSON(&req); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}

		if fraudLatencyMS > 0 {
			time.Sleep(time.Duration(fraudLatencyMS) * time.Millisecond)
		}

		var riskScore float64
		var decision string
		switch {
		case req.Amount > 10000:
			riskScore = 0.95
			decision = "DENY"
		case req.Amount > 2000:
			riskScore = 0.65
			decision = "FLAG"
		default:
			riskScore = 0.10
			decision = "APPROVE"
		}

		logEntry("info", serviceName, "fraud check result", map[string]any{
			"payment_id": req.PaymentID,
			"amount":     req.Amount,
			"risk_score": riskScore,
			"decision":   decision,
		})

		c.JSON(http.StatusOK, gin.H{"risk_score": riskScore, "decision": decision})
	})

	logEntry("info", serviceName, "starting", map[string]any{"port": port})
	if err := r.Run(":" + port); err != nil {
		logEntry("error", serviceName, "server failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
}
