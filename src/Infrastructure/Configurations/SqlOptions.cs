namespace ESP.DocumentExtractor.Infrastructure.Configurations;

public sealed class SqlOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 30;
}
