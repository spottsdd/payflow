namespace SettlementService;

public record SettleRequest(
    Guid PaymentId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string Currency,
    string TransactionId
);

public record SettleResponse(Guid SettlementId, DateTime SettledAt);
