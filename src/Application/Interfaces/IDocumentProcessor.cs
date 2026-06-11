using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IDocumentProcessor
{
    DocumentType DocumentType { get; }
    Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken);
}
