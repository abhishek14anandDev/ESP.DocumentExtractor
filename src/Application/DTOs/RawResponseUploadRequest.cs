namespace ESP.DocumentExtractor.Application.DTOs;

public sealed class RawResponseUploadRequest
{
    public required string BlobName { get; init; }
    public required string CorrelationId { get; init; }
    public required string Content { get; init; }
}
