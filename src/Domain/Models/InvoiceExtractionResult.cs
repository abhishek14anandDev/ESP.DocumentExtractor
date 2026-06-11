using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;

namespace ESP.DocumentExtractor.Domain.Models;

public sealed class InvoiceExtractionResult
{
    public bool IsSuccessful { get; init; }
    public DocumentType DocumentType { get; init; }
    public InvoiceHeader Invoice { get; init; } = new();
    public string? RawProviderResponse { get; init; }
    public string? ProcessorName { get; init; }
    public IReadOnlyCollection<string> ValidationErrors { get; init; } = [];
    public string? ErrorMessage { get; init; }
}
