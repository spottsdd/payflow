package com.payflow.gateway;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.client.HttpStatusCodeException;
import org.springframework.web.client.RestTemplate;

import java.math.BigDecimal;
import java.util.Map;

@RestController
public class Controller {

    private final RestTemplate restTemplate;
    private final String orchestratorUrl;

    public Controller(
            RestTemplate restTemplate,
            @Value("${orchestrator.url}") String orchestratorUrl) {
        this.restTemplate = restTemplate;
        this.orchestratorUrl = orchestratorUrl;
    }

    @GetMapping("/health")
    public ResponseEntity<Map<String, String>> health() {
        return ResponseEntity.ok(Map.of("status", "ok"));
    }

    @PostMapping("/payments")
    public ResponseEntity<String> createPayment(@RequestBody PaymentRequest body) {
        if (body == null) {
            return ResponseEntity.badRequest().body("{\"error\":\"request body is required\"}");
        }

        if (isBlank(body.fromAccountId())) {
            return badRequest("from_account_id is required");
        }
        if (isBlank(body.toAccountId())) {
            return badRequest("to_account_id is required");
        }
        if (body.amount() == null) {
            return badRequest("amount is required");
        }
        if (isBlank(body.currency())) {
            return badRequest("currency is required");
        }
        if (body.amount().compareTo(BigDecimal.ZERO) <= 0) {
            return badRequest("amount must be greater than 0");
        }
        if (body.fromAccountId().equals(body.toAccountId())) {
            return badRequest("from_account_id and to_account_id must be different");
        }
        if (body.currency().length() != 3) {
            return badRequest("currency must be a 3-character code");
        }

        try {
            ResponseEntity<String> response = restTemplate.postForEntity(
                    orchestratorUrl + "/payments", body, String.class);
            return ResponseEntity.status(response.getStatusCode()).body(response.getBody());
        } catch (HttpStatusCodeException e) {
            return ResponseEntity.status(e.getStatusCode()).body(e.getResponseBodyAsString());
        }
    }

    @GetMapping("/payments/{id}")
    public ResponseEntity<String> getPayment(@PathVariable String id) {
        try {
            ResponseEntity<String> response = restTemplate.getForEntity(
                    orchestratorUrl + "/payments/" + id, String.class);
            return ResponseEntity.status(response.getStatusCode()).body(response.getBody());
        } catch (HttpStatusCodeException e) {
            return ResponseEntity.status(e.getStatusCode()).body(e.getResponseBodyAsString());
        }
    }

    private ResponseEntity<String> badRequest(String message) {
        return ResponseEntity.status(HttpStatus.BAD_REQUEST)
                .body("{\"error\":\"" + message + "\"}");
    }

    private boolean isBlank(String value) {
        return value == null || value.isBlank();
    }

    record PaymentRequest(
            @com.fasterxml.jackson.annotation.JsonProperty("from_account_id") String fromAccountId,
            @com.fasterxml.jackson.annotation.JsonProperty("to_account_id") String toAccountId,
            BigDecimal amount,
            String currency) {
    }
}
