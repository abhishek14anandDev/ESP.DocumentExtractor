namespace ESP.DocumentExtractor.Application.DTOs;

public sealed class DocumentProcessingRequest
{
    public required string BlobName { get; init; }
    public required string ContainerName { get; init; }
    public Uri? BlobUri { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public string? EventType { get; init; }
    public string ExtractionMode { get; init; } = "Normal";
}
