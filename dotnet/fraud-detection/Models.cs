namespace FraudDetection;

public record FraudCheckRequest(Guid PaymentId, decimal Amount, string Currency);

public record FraudCheckResponse(double RiskScore, string Decision);
