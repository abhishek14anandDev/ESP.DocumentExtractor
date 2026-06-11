using ESP.DocumentExtractor.Domain.Enums;

namespace ESP.DocumentExtractor.Domain.Entities;

public sealed class InvoiceHeader
{
    public long InvoiceHeaderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string? VendorAddress { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Currency { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? PaymentTerms { get; set; }
    public string? CustomerName { get; set; }
    public decimal InvoiceConfidenceScore { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceFileType { get; set; } = string.Empty;
    public DateTimeOffset ProcessingDate { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedDate { get; set; } = DateTimeOffset.UtcNow;
    public List<InvoiceLineItem> LineItems { get; init; } = [];
}
