using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Strategies;

public sealed class WordProcessor(IInvoiceExtractionService invoiceExtractionService) : IWordProcessor
{
    public DocumentType DocumentType => DocumentType.Word;

    public Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken) =>
        invoiceExtractionService.ExtractInvoiceAsync(document, cancellationToken);
}
