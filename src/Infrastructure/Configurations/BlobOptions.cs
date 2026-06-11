namespace ESP.DocumentExtractor.Infrastructure.Configurations;

public sealed class BlobOptions
{
    public string IncomingContainer { get; init; } = "incoming";
    public string ProcessedContainer { get; init; } = "processed";
    public string RejectedContainer { get; init; } = "rejected";
    public string AuditContainer { get; init; } = "audit";
    public string RawResponsesContainer { get; init; } = "rawresponses";
}
