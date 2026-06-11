namespace ESP.DocumentExtractor.Domain.Models;

public sealed class BlobDocument
{
    public string BlobName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] Content { get; init; } = [];
    public string? ETag { get; init; }
    public string? CorrelationId { get; init; }
    public string ExtractionMode { get; init; } = "Normal";
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
