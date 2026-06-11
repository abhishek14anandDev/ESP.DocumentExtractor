using ESP.DocumentExtractor.Domain.Enums;

namespace ESP.DocumentExtractor.Application.DTOs;

public sealed class DocumentProcessingResponse
{
    public required string CorrelationId { get; init; }
    public required string BlobName { get; init; }
    public required string ContainerName { get; init; }
    public required DocumentType DocumentType { get; init; }
    public required ProcessingStatus ProcessingStatus { get; init; }
    public long? InvoiceHeaderId { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}
