package com.payflow.processor.controller;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.client.HttpStatusCodeException;
import org.springframework.web.client.RestTemplate;

import java.util.Map;

@RestController
public class ProcessorController {

    private final RestTemplate restTemplate;
    private final String gatewayStubUrl;

    public ProcessorController(
            RestTemplate restTemplate,
            @Value("${gateway.stub.url}") String gatewayStubUrl) {
        this.restTemplate = restTemplate;
        this.gatewayStubUrl = gatewayStubUrl;
    }

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "ok");
    }

    @PostMapping("/process")
    public ResponseEntity<Object> process(@RequestBody Map<String, Object> body) {
        Map<String, Object> chargePayload = Map.of(
                "payment_id", body.get("payment_id"),
                "amount", body.get("amount"),
                "currency", body.get("currency"));

        try {
            ResponseEntity<Object> response = restTemplate.postForEntity(
                    gatewayStubUrl + "/charge", chargePayload, Object.class);
            return ResponseEntity.status(response.getStatusCode()).body(response.getBody());
        } catch (HttpStatusCodeException e) {
            return ResponseEntity.status(e.getStatusCode()).body(e.getResponseBodyAsString());
        }
    }
}
