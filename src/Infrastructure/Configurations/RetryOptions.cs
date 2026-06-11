namespace ESP.DocumentExtractor.Infrastructure.Configurations;

public sealed class RetryOptions
{
    public int MaxAttempts { get; init; } = 3;
    public int BaseDelayMilliseconds { get; init; } = 250;
    public double BackoffMultiplier { get; init; } = 2.0d;
}
