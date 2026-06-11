using ESP.DocumentExtractor.Domain.Enums;

namespace ESP.DocumentExtractor.Domain.Entities;

public sealed class DocumentProcessingAudit
{
    public long DocumentProcessingAuditId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public long? InvoiceHeaderId { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public DateTimeOffset ProcessedOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedDate { get; set; } = DateTimeOffset.UtcNow;
}
