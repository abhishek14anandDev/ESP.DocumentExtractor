namespace ESP.DocumentExtractor.Infrastructure.Configurations;

public sealed class DocumentIntelligenceOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ModelId { get; init; } = "prebuilt-invoice";
}
