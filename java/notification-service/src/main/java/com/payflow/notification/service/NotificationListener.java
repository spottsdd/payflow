package com.payflow.notification.service;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.kafka.support.KafkaHeaders;
import org.springframework.messaging.handler.annotation.Header;
import org.springframework.messaging.handler.annotation.Payload;
import org.springframework.stereotype.Service;

import java.util.Map;

@Service
public class NotificationListener {

    private static final Logger log = LoggerFactory.getLogger(NotificationListener.class);

    private static final String INSERT_SQL =
        "INSERT INTO notifications (id, payment_id, type, payload, created_at) " +
        "VALUES (gen_random_uuid(), ?::uuid, ?, ?::jsonb, NOW())";

    private final JdbcTemplate jdbc;
    private final ObjectMapper objectMapper;

    public NotificationListener(JdbcTemplate jdbc, ObjectMapper objectMapper) {
        this.jdbc = jdbc;
        this.objectMapper = objectMapper;
    }

    @KafkaListener(topics = "payment.completed")
    public void onPaymentCompleted(@Payload String message) {
        try {
            Map<?, ?> payload = objectMapper.readValue(message, Map.class);
            String paymentId = (String) payload.get("payment_id");
            jdbc.update(INSERT_SQL, paymentId, "PAYMENT_COMPLETED", message);
        } catch (Exception e) {
            log.error("Failed to process payment.completed message: {}", e.getMessage(), e);
        }
    }

    @KafkaListener(topics = "payment.failed")
    public void onPaymentFailed(@Payload String message) {
        try {
            Map<?, ?> payload = objectMapper.readValue(message, Map.class);
            String paymentId = (String) payload.get("payment_id");
            jdbc.update(INSERT_SQL, paymentId, "PAYMENT_FAILED", message);
        } catch (Exception e) {
            log.error("Failed to process payment.failed message: {}", e.getMessage(), e);
        }
    }
}
