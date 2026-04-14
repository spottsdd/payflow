namespace PaymentProcessor;

public record ProcessRequest(Guid PaymentId, decimal Amount, string Currency);

public record ProcessResponse(string Status, string TransactionId);

public record GatewayChargeRequest(Guid PaymentId, decimal Amount, string Currency);

public record GatewayChargeResponse(string Status, string TransactionId);
