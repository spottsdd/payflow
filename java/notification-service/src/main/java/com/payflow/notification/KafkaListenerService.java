package com.payflow.notification;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Service;

@Service
public class KafkaListenerService {

    private static final Logger log = LoggerFactory.getLogger(KafkaListenerService.class);

    private static final String INSERT_NOTIFICATION = """
            INSERT INTO notifications (id, payment_id, type, payload, created_at)
            VALUES (gen_random_uuid(), ?::uuid, ?, ?::jsonb, NOW())
            """;

    private final JdbcTemplate jdbcTemplate;
    private final ObjectMapper objectMapper = new ObjectMapper();

    public KafkaListenerService(JdbcTemplate jdbcTemplate) {
        this.jdbcTemplate = jdbcTemplate;
    }

    @KafkaListener(topics = {"payment.completed", "payment.failed"}, groupId = "notification-service")
    public void handlePaymentEvent(ConsumerRecord<String, String> record) {
        String topic = record.topic();
        String payload = record.value();

        try {
            JsonNode json = objectMapper.readTree(payload);
            String paymentId = json.get("payment_id").asText();

            String type = topic.equals("payment.completed") ? "PAYMENT_COMPLETED" : "PAYMENT_FAILED";

            jdbcTemplate.update(INSERT_NOTIFICATION, paymentId, type, payload);

            log.info("Recorded notification type={} paymentId={}", type, paymentId);
        } catch (Exception e) {
            log.error("Failed to process event from topic={} payload={}", topic, payload, e);
        }
    }
}
