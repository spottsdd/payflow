namespace ApiGateway;

public record PaymentRequest(
    Guid? FromAccountId,
    Guid? ToAccountId,
    decimal? Amount,
    string? Currency
);
