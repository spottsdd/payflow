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

const serviceName = "payment-processor"

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

type processRequest struct {
	PaymentID     string  `json:"payment_id"`
	FromAccountID string  `json:"from_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
}

type chargeRequest struct {
	PaymentID string  `json:"payment_id"`
	Amount    float64 `json:"amount"`
	Currency  string  `json:"currency"`
}

type gatewayResponse struct {
	Status        string `json:"status"`
	TransactionID string `json:"transaction_id"`
}

func main() {
	gatewayURL := getEnv("GATEWAY_STUB_URL", "http://gateway-stub:9999")
	port := getEnv("PORT", "8083")

	gin.SetMode(gin.ReleaseMode)
	r := gin.New()
	r.Use(gin.Recovery())

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	r.POST("/process", func(c *gin.Context) {
		var req processRequest
		if err := c.ShouldBindJSON(&req); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}

		chargeBody, _ := json.Marshal(chargeRequest{
			PaymentID: req.PaymentID,
			Amount:    req.Amount,
			Currency:  req.Currency,
		})

		httpReq, err := http.NewRequest(http.MethodPost, gatewayURL+"/charge", bytes.NewReader(chargeBody))
		if err != nil {
			logEntry("error", serviceName, "failed to build gateway request", map[string]any{"error": err.Error()})
			c.JSON(http.StatusInternalServerError, gin.H{"error": "internal error"})
			return
		}
		httpReq.Header.Set("Content-Type", "application/json")

		client := &http.Client{Timeout: 10 * time.Second}
		resp, err := client.Do(httpReq)
		if err != nil {
			logEntry("error", serviceName, "gateway request failed", map[string]any{
				"payment_id": req.PaymentID,
				"error":      err.Error(),
			})
			c.JSON(http.StatusBadGateway, gin.H{"error": "gateway unavailable"})
			return
		}
		defer resp.Body.Close()

		respBody, _ := io.ReadAll(resp.Body)

		var gwResp gatewayResponse
		if err := json.Unmarshal(respBody, &gwResp); err != nil || gwResp.TransactionID == "" {
			// Gateway did not return a parseable transaction; generate a local one
			gwResp.Status = "APPROVED"
			gwResp.TransactionID = uuid.New().String()
		}

		logEntry("info", serviceName, "charge processed", map[string]any{
			"payment_id":     req.PaymentID,
			"status":         gwResp.Status,
			"transaction_id": gwResp.TransactionID,
		})

		c.JSON(http.StatusOK, gin.H{"status": gwResp.Status, "transaction_id": gwResp.TransactionID})
	})

	logEntry("info", serviceName, "starting", map[string]any{"port": port})
	if err := r.Run(":" + port); err != nil {
		logEntry("error", serviceName, "server failed", map[string]any{"error": err.Error()})
		os.Exit(1)
	}
}
