package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

const serviceName = "api-gateway"

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

type paymentRequest struct {
	FromAccountID string  `json:"from_account_id"`
	ToAccountID   string  `json:"to_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
}

func validatePaymentRequest(req paymentRequest) string {
	if req.FromAccountID == "" {
		return "from_account_id is required"
	}
	if req.ToAccountID == "" {
		return "to_account_id is required"
	}
	if req.Amount <= 0 {
		return "amount must be greater than 0"
	}
	if len(req.Currency) != 3 {
		return "currency must be a 3-character code"
	}
	if req.FromAccountID == req.ToAccountID {
		return "from_account_id and to_account_id must be different"
	}
	if _, err := uuid.Parse(req.FromAccountID); err != nil {
		return "from_account_id must be a valid UUID"
	}
	if _, err := uuid.Parse(req.ToAccountID); err != nil {
		return "to_account_id must be a valid UUID"
	}
	return ""
}

func proxyRequest(c *gin.Context, method, url string, body io.Reader) {
	req, err := http.NewRequest(method, url, body)
	if err != nil {
		logEntry("error", serviceName, "failed to build proxy request", map[string]any{"error": err.Error(), "url": url})
		c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
		return
	}
	req.Header.Set("Content-Type", "application/json")

	client := &http.Client{Timeout: 30 * time.Second}
	resp, err := client.Do(req)
	if err != nil {
		logEntry("error", serviceName, "upstream request failed", map[string]any{"error": err.Error(), "url": url})
		c.JSON(http.StatusBadGateway, gin.H{"error": "upstream unavailable"})
		return
	}
	defer resp.Body.Close()

	respBody, err := io.ReadAll(resp.Body)
	if err != nil {
		logEntry("error", serviceName, "failed to read upstream response", map[string]any{"error": err.Error()})
		c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
		return
	}

	c.Data(resp.StatusCode, "application/json", respBody)
}

func main() {
	orchestratorURL := getEnv("ORCHESTRATOR_URL", "http://payment-orchestrator:8081")
	port := getEnv("PORT", "8080")

	gin.SetMode(gin.ReleaseMode)
	r := gin.New()
	r.Use(gin.Recovery())

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	r.POST("/payments", func(c *gin.Context) {
		var req paymentRequest
		if err := c.ShouldBindJSON(&req); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}
		if msg := validatePaymentRequest(req); msg != "" {
			c.JSON(http.StatusBadRequest, gin.H{"error": msg})
			return
		}

		b, _ := json.Marshal(req)
		logEntry("info", serviceName, "forwarding payment request", map[string]any{
			"from_account_id": req.FromAccountID,
			"to_account_id":   req.ToAccountID,
			"amount":          req.Amount,
			"currency":        req.Currency,
		})
		proxyRequest(c, http.MethodPost, orchestratorURL+"/payments", bytes.NewReader(b))
	})

	r.GET("/payments/:id", func(c *gin.Context) {
		id := c.Param("id")
		proxyRequest(c, http.MethodGet, orchestratorURL+"/payments/"+id, nil)
	})

	logEntry("info", serviceName, "starting", map[string]any{"port": port})
	if err := r.Run(":" + port); err != nil {
		logEntry("error", serviceName, "server failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
}
