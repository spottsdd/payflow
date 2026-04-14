package com.payflow.settlement;

import org.springframework.http.ResponseEntity;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

import java.math.BigDecimal;
import java.util.Map;

@RestController
public class Controller {

    private final JdbcTemplate jdbc;
    private final TransactionTemplate txTemplate;

    public Controller(JdbcTemplate jdbc, DataSourceTransactionManager txManager) {
        this.jdbc = jdbc;
        this.txTemplate = new TransactionTemplate(txManager);
    }

    @GetMapping("/health")
    public ResponseEntity<Map<String, String>> health() {
        return ResponseEntity.ok(Map.of("status", "ok"));
    }

    @PostMapping("/settle")
    public ResponseEntity<Map<String, Object>> settle(@RequestBody Map<String, Object> body) {
        String fromAccountId = (String) body.get("from_account_id");
        String toAccountId = (String) body.get("to_account_id");
        String paymentId = (String) body.get("payment_id");
        BigDecimal amount = new BigDecimal(body.get("amount").toString());

        try {
            Map<String, Object> result = txTemplate.execute(status -> {
                Map<String, Object> account = jdbc.queryForMap(
                    "SELECT balance FROM accounts WHERE id = ?::uuid FOR UPDATE",
                    fromAccountId
                );
                BigDecimal balance = (BigDecimal) account.get("balance");

                if (balance.compareTo(amount) < 0) {
                    throw new InsufficientFundsException();
                }

                jdbc.update(
                    "UPDATE accounts SET balance = balance - ? WHERE id = ?::uuid",
                    amount, fromAccountId
                );
                jdbc.update(
                    "UPDATE accounts SET balance = balance + ? WHERE id = ?::uuid",
                    amount, toAccountId
                );

                return jdbc.queryForMap(
                    "INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at) " +
                    "VALUES (gen_random_uuid(), ?::uuid, ?::uuid, ?::uuid, ?, NOW()) " +
                    "RETURNING id, settled_at",
                    paymentId, fromAccountId, toAccountId, amount
                );
            });

            return ResponseEntity.ok(Map.of(
                "settlement_id", result.get("id").toString(),
                "settled_at", result.get("settled_at").toString()
            ));
        } catch (InsufficientFundsException e) {
            return ResponseEntity.status(402).body(Map.of("error", "insufficient funds"));
        }
    }

    private static class InsufficientFundsException extends RuntimeException {
    }
}
