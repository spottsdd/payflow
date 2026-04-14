package com.payflow.fraud.controller;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.web.bind.annotation.*;

import java.math.BigDecimal;
import java.util.Map;

@RestController
public class FraudController {

    private final long latencyMs;

    public FraudController(@Value("${fraud.latency.ms:0}") long latencyMs) {
        this.latencyMs = latencyMs;
    }

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "ok");
    }

    @PostMapping("/fraud/check")
    public Map<String, Object> check(@RequestBody Map<String, Object> body) throws InterruptedException {
        if (latencyMs > 0) {
            Thread.sleep(latencyMs);
        }

        BigDecimal amount = new BigDecimal(body.get("amount").toString());

        if (amount.compareTo(new BigDecimal("10000")) > 0) {
            return Map.of("risk_score", 0.95, "decision", "DENY");
        }
        if (amount.compareTo(new BigDecimal("2000")) > 0) {
            return Map.of("risk_score", 0.65, "decision", "FLAG");
        }
        return Map.of("risk_score", 0.10, "decision", "APPROVE");
    }
}
