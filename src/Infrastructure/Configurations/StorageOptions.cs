namespace ESP.DocumentExtractor.Infrastructure.Configurations;

public sealed class StorageOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public Uri? ServiceUri { get; init; }
}
