package com.payflow.orchestrator;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.ResponseEntity;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.client.HttpStatusCodeException;
import org.springframework.web.client.ResourceAccessException;
import org.springframework.web.client.RestTemplate;

import java.math.BigDecimal;
import java.util.Map;
import java.util.UUID;

@RestController
public class Controller {

    private final JdbcTemplate jdbc;
    private final RestTemplate restTemplate;
    private final KafkaTemplate<String, String> kafkaTemplate;
    private final String fraudServiceUrl;
    private final String processorServiceUrl;
    private final String settlementServiceUrl;
    private final ObjectMapper objectMapper = new ObjectMapper();

    public Controller(
        JdbcTemplate jdbc,
        RestTemplate restTemplate,
        KafkaTemplate<String, String> kafkaTemplate,
        @Value("${fraud.service.url}") String fraudServiceUrl,
        @Value("${processor.service.url}") String processorServiceUrl,
        @Value("${settlement.service.url}") String settlementServiceUrl,
        @Value("${fraud.timeout.ms:5000}") long fraudTimeoutMs,
        @Value("${processor.timeout.ms:10000}") long processorTimeoutMs,
        @Value("${settlement.timeout.ms:5000}") long settlementTimeoutMs
    ) {
        this.jdbc = jdbc;
        this.restTemplate = restTemplate;
        this.kafkaTemplate = kafkaTemplate;
        this.fraudServiceUrl = fraudServiceUrl;
        this.processorServiceUrl = processorServiceUrl;
        this.settlementServiceUrl = settlementServiceUrl;
    }

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "ok");
    }

    @PostMapping("/payments")
    public ResponseEntity<Map<String, Object>> createPayment(@RequestBody Map<String, Object> body) {
        String paymentId = UUID.randomUUID().toString();
        String fromAccountId = (String) body.get("from_account_id");
        String toAccountId = (String) body.get("to_account_id");
        BigDecimal amount = new BigDecimal(body.get("amount").toString());
        String currency = (String) body.get("currency");

        jdbc.update(
            "INSERT INTO payments (id, from_account_id, to_account_id, amount, currency, status, created_at, updated_at) VALUES (?::uuid, ?, ?, ?, ?, 'PENDING', NOW(), NOW())",
            paymentId, fromAccountId, toAccountId, amount, currency
        );

        // Fraud check
        try {
            ResponseEntity<Map> fraudResponse = restTemplate.postForEntity(
                fraudServiceUrl + "/fraud/check",
                Map.of("payment_id", paymentId, "from_account_id", fromAccountId, "amount", amount, "currency", currency),
                Map.class
            );
            Map<?, ?> fraudBody = fraudResponse.getBody();
            if (fraudBody != null && "DENY".equals(fraudBody.get("decision"))) {
                jdbc.update("UPDATE payments SET status='DECLINED', updated_at=NOW() WHERE id=?::uuid", paymentId);
                publish("payment.failed", Map.of("payment_id", paymentId, "reason", "fraud_denied"));
                return ResponseEntity.ok(Map.of("payment_id", paymentId, "status", "DECLINED", "transaction_id", ""));
            }
        } catch (Exception e) {
            jdbc.update("UPDATE payments SET status='FAILED', updated_at=NOW() WHERE id=?::uuid", paymentId);
            publish("payment.failed", Map.of("payment_id", paymentId, "reason", "service_error"));
            return ResponseEntity.internalServerError().body(Map.of("error", "fraud check failed"));
        }

        // Processor
        String transactionId;
        try {
            ResponseEntity<Map> processorResponse = restTemplate.postForEntity(
                processorServiceUrl + "/process",
                Map.of("payment_id", paymentId, "from_account_id", fromAccountId, "amount", amount, "currency", currency),
                Map.class
            );
            Map<?, ?> processorBody = processorResponse.getBody();
            if (processorBody != null && "DECLINED".equals(processorBody.get("status"))) {
                jdbc.update("UPDATE payments SET status='FAILED', updated_at=NOW() WHERE id=?::uuid", paymentId);
                publish("payment.failed", Map.of("payment_id", paymentId, "reason", "processor_declined"));
                return ResponseEntity.ok(Map.of("payment_id", paymentId, "status", "FAILED", "transaction_id", ""));
            }
            transactionId = processorBody != null ? (String) processorBody.get("transaction_id") : "";
        } catch (Exception e) {
            jdbc.update("UPDATE payments SET status='FAILED', updated_at=NOW() WHERE id=?::uuid", paymentId);
            publish("payment.failed", Map.of("payment_id", paymentId, "reason", "service_error"));
            return ResponseEntity.internalServerError().body(Map.of("error", "processor failed"));
        }

        // Settlement
        try {
            restTemplate.postForEntity(
                settlementServiceUrl + "/settle",
                Map.of(
                    "payment_id", paymentId,
                    "from_account_id", fromAccountId,
                    "to_account_id", toAccountId,
                    "amount", amount,
                    "currency", currency,
                    "transaction_id", transactionId
                ),
                Map.class
            );
        } catch (HttpStatusCodeException e) {
            jdbc.update("UPDATE payments SET status='FAILED', updated_at=NOW() WHERE id=?::uuid", paymentId);
            if (e.getStatusCode().value() == 402) {
                publish("payment.failed", Map.of("payment_id", paymentId, "reason", "insufficient_funds"));
                return ResponseEntity.ok(Map.of("payment_id", paymentId, "status", "FAILED", "transaction_id", transactionId));
            }
            publish("payment.failed", Map.of("payment_id", paymentId, "reason", "service_error"));
            return ResponseEntity.internalServerError().body(Map.of("error", "settlement failed"));
        } catch (Exception e) {
            jdbc.update("UPDATE payments SET status='FAILED', updated_at=NOW() WHERE id=?::uuid", paymentId);
            publish("payment.failed", Map.of("payment_id", paymentId, "reason", "service_error"));
            return ResponseEntity.internalServerError().body(Map.of("error", "settlement failed"));
        }

        jdbc.update("UPDATE payments SET status='COMPLETED', updated_at=NOW() WHERE id=?::uuid", paymentId);
        publish("payment.completed", Map.of("payment_id", paymentId, "amount", amount, "from_account_id", fromAccountId));

        return ResponseEntity.ok(Map.of("payment_id", paymentId, "status", "COMPLETED", "transaction_id", transactionId));
    }

    @GetMapping("/payments/{id}")
    public ResponseEntity<Map<String, Object>> getPayment(@PathVariable String id) {
        try {
            Map<String, Object> row = jdbc.queryForObject(
                "SELECT id, status, amount, currency, created_at FROM payments WHERE id=?::uuid",
                (rs, rowNum) -> Map.of(
                    "payment_id", rs.getString("id"),
                    "status", rs.getString("status"),
                    "amount", rs.getBigDecimal("amount"),
                    "currency", rs.getString("currency"),
                    "created_at", rs.getTimestamp("created_at").toString()
                ),
                id
            );
            return ResponseEntity.ok(row);
        } catch (Exception e) {
            return ResponseEntity.status(404).body(Map.of("error", "not found"));
        }
    }

    private void publish(String topic, Map<String, Object> payload) {
        try {
            kafkaTemplate.send(topic, objectMapper.writeValueAsString(payload));
        } catch (JsonProcessingException e) {
            throw new RuntimeException("Failed to serialize Kafka message", e);
        }
    }
}
