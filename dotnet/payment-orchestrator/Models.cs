namespace PaymentOrchestrator;

public record PaymentRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, string Currency);

public record PaymentResponse(Guid PaymentId, string Status, string TransactionId);

public record PaymentRecord(
    Guid Id,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record FraudCheckRequest(Guid PaymentId, decimal Amount, string Currency);

public record FraudCheckResponse(double RiskScore, string Decision);

public record ProcessRequest(Guid PaymentId, decimal Amount, string Currency);

public record ProcessResponse(string Status, string TransactionId);

public record SettleRequest(
    Guid PaymentId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string Currency,
    string TransactionId
);
