package com.payflow.settlement.controller;

import com.payflow.settlement.InsufficientFundsException;
import com.payflow.settlement.service.SettlementService;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

import java.util.Map;

@RestController
public class SettlementController {

    private final SettlementService settlementService;

    public SettlementController(SettlementService settlementService) {
        this.settlementService = settlementService;
    }

    @PostMapping("/settle")
    public ResponseEntity<Map<String, Object>> settle(@RequestBody Map<String, Object> body) {
        try {
            return ResponseEntity.ok(settlementService.settle(body));
        } catch (InsufficientFundsException e) {
            return ResponseEntity.status(402).body(Map.of("error", "insufficient funds"));
        }
    }

    @GetMapping("/health")
    public ResponseEntity<Map<String, String>> health() {
        return ResponseEntity.ok(Map.of("status", "ok"));
    }
}
