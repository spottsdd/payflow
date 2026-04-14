package com.payflow.settlement.service;

import com.payflow.settlement.InsufficientFundsException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.math.BigDecimal;
import java.util.List;
import java.util.Map;

@Service
public class SettlementService {

    private final JdbcTemplate jdbc;

    public SettlementService(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @Transactional
    public Map<String, Object> settle(Map<String, Object> body) {
        Object fromAccountId = body.get("from_account_id");
        Object toAccountId = body.get("to_account_id");
        Object paymentId = body.get("payment_id");
        double amount = ((Number) body.get("amount")).doubleValue();

        List<BigDecimal> rows = jdbc.query(
            "SELECT balance FROM accounts WHERE id = ? FOR UPDATE",
            (rs, rowNum) -> rs.getBigDecimal("balance"),
            fromAccountId
        );

        if (rows.isEmpty()) {
            throw new RuntimeException("account not found: " + fromAccountId);
        }

        BigDecimal balance = rows.get(0);
        if (balance.doubleValue() < amount) {
            throw new InsufficientFundsException();
        }

        jdbc.update(
            "UPDATE accounts SET balance = balance - ? WHERE id = ?",
            amount, fromAccountId
        );
        jdbc.update(
            "UPDATE accounts SET balance = balance + ? WHERE id = ?",
            amount, toAccountId
        );

        return jdbc.queryForMap(
            "INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at) " +
            "VALUES (gen_random_uuid(), ?, ?, ?, ?, NOW()) RETURNING id AS settlement_id, settled_at",
            paymentId, fromAccountId, toAccountId, amount
        );
    }
}
