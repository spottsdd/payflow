package com.payflow.settlement.service;

import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.stereotype.Service;
import org.springframework.transaction.support.TransactionTemplate;

import java.math.BigDecimal;
import java.util.Map;

@Service
public class SettlementService {

    private final JdbcTemplate jdbc;
    private final TransactionTemplate txTemplate;

    public SettlementService(JdbcTemplate jdbc, DataSourceTransactionManager txManager) {
        this.jdbc = jdbc;
        this.txTemplate = new TransactionTemplate(txManager);
    }

    public Map<String, Object> settle(Map<String, Object> body) {
        String fromAccountId = (String) body.get("from_account_id");
        String toAccountId = (String) body.get("to_account_id");
        String paymentId = (String) body.get("payment_id");
        BigDecimal amount = new BigDecimal(body.get("amount").toString());

        Map<String, Object> result = txTemplate.execute(status -> {
            Map<String, Object> account = jdbc.queryForMap(
                    "SELECT balance FROM accounts WHERE id = ?::uuid FOR UPDATE",
                    fromAccountId);
            BigDecimal balance = (BigDecimal) account.get("balance");

            if (balance.compareTo(amount) < 0) {
                throw new InsufficientFundsException();
            }

            jdbc.update("UPDATE accounts SET balance = balance - ? WHERE id = ?::uuid", amount, fromAccountId);
            jdbc.update("UPDATE accounts SET balance = balance + ? WHERE id = ?::uuid", amount, toAccountId);

            return jdbc.queryForMap(
                    "INSERT INTO settlements (id, payment_id, from_account_id, to_account_id, amount, settled_at) "
                            + "VALUES (gen_random_uuid(), ?::uuid, ?::uuid, ?::uuid, ?, NOW()) "
                            + "RETURNING id AS settlement_id, settled_at",
                    paymentId, fromAccountId, toAccountId, amount);
        });

        return Map.of(
                "settlement_id", result.get("settlement_id").toString(),
                "settled_at", result.get("settled_at").toString());
    }
}
