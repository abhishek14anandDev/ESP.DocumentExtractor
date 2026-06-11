namespace ESP.DocumentExtractor.Domain.Entities;

public sealed class InvoiceLineItem
{
    public long InvoiceLineItemId { get; set; }
    public long InvoiceHeaderId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Tax { get; set; }
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedDate { get; set; } = DateTimeOffset.UtcNow;
}
