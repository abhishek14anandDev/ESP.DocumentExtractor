using ESP.DocumentExtractor.Domain.Enums;

namespace ESP.DocumentExtractor.Domain.Entities;

public sealed class BlobProcessingHistory
{
    public long BlobProcessingHistoryId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string SourceContainer { get; set; } = string.Empty;
    public string? DestinationContainer { get; set; }
    public DocumentType DocumentType { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ProcessedOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedDate { get; set; } = DateTimeOffset.UtcNow;
}
