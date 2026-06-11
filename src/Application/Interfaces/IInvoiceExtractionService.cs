using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IInvoiceExtractionService
{
    Task<Result<InvoiceExtractionResult>> ExtractInvoiceAsync(BlobDocument document, CancellationToken cancellationToken);
}
