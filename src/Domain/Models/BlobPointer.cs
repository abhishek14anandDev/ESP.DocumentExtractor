namespace ESP.DocumentExtractor.Domain.Models;

public sealed class BlobPointer
{
    public required string BlobName { get; init; }
    public required string ContainerName { get; init; }
    public string? ContentType { get; init; }
    public Uri? BlobUri { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
