package com.payflow.fraud;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

import java.math.BigDecimal;
import java.util.Map;

@RestController
public class Controller {

    private final long fraudLatencyMs;

    public Controller(@Value("${fraud.latency.ms:0}") long fraudLatencyMs) {
        this.fraudLatencyMs = fraudLatencyMs;
    }

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "ok");
    }

    @PostMapping("/fraud/check")
    public Map<String, Object> checkFraud(@RequestBody Map<String, Object> body)
            throws InterruptedException {
        if (fraudLatencyMs > 0) {
            Thread.sleep(fraudLatencyMs);
        }

        BigDecimal amount = new BigDecimal(body.get("amount").toString());

        double riskScore;
        String decision;

        if (amount.compareTo(new BigDecimal("10000")) > 0) {
            riskScore = 0.95;
            decision = "DENY";
        } else if (amount.compareTo(new BigDecimal("2000")) > 0) {
            riskScore = 0.65;
            decision = "FLAG";
        } else {
            riskScore = 0.10;
            decision = "APPROVE";
        }

        return Map.of("risk_score", riskScore, "decision", decision);
    }
}
