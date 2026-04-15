package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"math/rand"
	"net/http"
	"os"
	"strconv"
	"sync"
	"time"
)

var accountIDs = []string{
	"a1000000-0000-0000-0000-000000000001",
	"a1000000-0000-0000-0000-000000000002",
	"a1000000-0000-0000-0000-000000000003",
	"a1000000-0000-0000-0000-000000000004",
	"a1000000-0000-0000-0000-000000000005",
	"a1000000-0000-0000-0000-000000000006",
	"a1000000-0000-0000-0000-000000000007",
	"a1000000-0000-0000-0000-000000000008",
	"a1000000-0000-0000-0000-000000000009",
	"a1000000-0000-0000-0000-000000000010",
}

type paymentRequest struct {
	FromAccountID string  `json:"from_account_id"`
	ToAccountID   string  `json:"to_account_id"`
	Amount        float64 `json:"amount"`
	Currency      string  `json:"currency"`
}

func randomPair() (string, string) {
	from := rand.Intn(len(accountIDs))
	to := rand.Intn(len(accountIDs) - 1)
	if to >= from {
		to++
	}
	return accountIDs[from], accountIDs[to]
}

// Traffic mix:
// 75% normal ($1–$500)      → fraud APPROVE
// 15% flagged ($2001–$9999) → fraud FLAG (threshold: >$2000)
// 10% denied (>$10000)      → fraud DENY (threshold: >$10000)
func randomAmount() float64 {
	r := rand.Float64()
	switch {
	case r < 0.75:
		return 1 + rand.Float64()*499
	case r < 0.90:
		return 2001 + rand.Float64()*7998
	default:
		return 10001 + rand.Float64()*9999
	}
}

func envInt(key string, def int) int {
	if v := os.Getenv(key); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			return n
		}
	}
	return def
}

func envStr(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}

func waitForGateway(url string) {
	healthURL := url + "/health"
	slog.Info("waiting for api-gateway", "url", healthURL)
	for {
		resp, err := http.Get(healthURL)
		if err == nil && resp.StatusCode == http.StatusOK {
			resp.Body.Close()
			slog.Info("api-gateway is ready")
			return
		}
		if resp != nil {
			resp.Body.Close()
		}
		time.Sleep(2 * time.Second)
	}
}

func sendPayment(client *http.Client, gatewayURL string) {
	from, to := randomPair()
	amount := randomAmount()

	payload := paymentRequest{
		FromAccountID: from,
		ToAccountID:   to,
		Amount:        amount,
		Currency:      "USD",
	}

	body, _ := json.Marshal(payload)
	start := time.Now()

	resp, err := client.Post(gatewayURL+"/payments", "application/json", bytes.NewReader(body))
	elapsed := time.Since(start).Milliseconds()

	if err != nil {
		slog.Error("request failed", "error", err.Error(), "latency_ms", elapsed)
		return
	}
	defer resp.Body.Close()
	io.Copy(io.Discard, resp.Body)

	slog.Info("payment sent",
		"from", from,
		"to", to,
		"amount", fmt.Sprintf("%.2f", amount),
		"status_code", resp.StatusCode,
		"latency_ms", elapsed,
	)
}

func worker(client *http.Client, gatewayURL string, ticker <-chan time.Time, done <-chan struct{}, wg *sync.WaitGroup) {
	defer wg.Done()
	for {
		select {
		case <-done:
			return
		case <-ticker:
			sendPayment(client, gatewayURL)
		}
	}
}

func main() {
	gatewayURL := envStr("GATEWAY_URL", "http://api-gateway:8080")
	rps := envInt("REQUESTS_PER_SECOND", 10)
	concurrency := envInt("CONCURRENCY", 5)
	duration := envInt("DURATION_SECONDS", 0)

	slog.Info("traffic-generator starting",
		"gateway_url", gatewayURL,
		"rps", rps,
		"concurrency", concurrency,
		"duration_seconds", duration,
	)

	waitForGateway(gatewayURL)

	client := &http.Client{Timeout: 10 * time.Second}
	intervalNs := time.Second / time.Duration(rps)
	ticker := time.NewTicker(intervalNs)
	defer ticker.Stop()

	done := make(chan struct{})
	var wg sync.WaitGroup

	for i := 0; i < concurrency; i++ {
		wg.Add(1)
		go worker(client, gatewayURL, ticker.C, done, &wg)
	}

	if duration > 0 {
		time.Sleep(time.Duration(duration) * time.Second)
		close(done)
	} else {
		select {}
	}

	wg.Wait()
	slog.Info("traffic-generator stopped")
}
