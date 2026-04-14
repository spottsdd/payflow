package com.payflow.settlement.controller;

import com.payflow.settlement.service.SettlementService;
import com.payflow.settlement.service.InsufficientFundsException;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.Map;

@RestController
public class SettlementController {

    private final SettlementService settlementService;

    public SettlementController(SettlementService settlementService) {
        this.settlementService = settlementService;
    }

    @GetMapping("/health")
    public Map<String, String> health() {
        return Map.of("status", "ok");
    }

    @PostMapping("/settle")
    public ResponseEntity<Map<String, Object>> settle(@RequestBody Map<String, Object> body) {
        try {
            Map<String, Object> result = settlementService.settle(body);
            return ResponseEntity.ok(result);
        } catch (InsufficientFundsException e) {
            return ResponseEntity.status(402).body(Map.of("error", "insufficient funds"));
        }
    }
}
