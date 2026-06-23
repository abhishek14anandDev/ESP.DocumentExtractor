using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Strategies;

public sealed class CadProcessor : ICadProcessor
{
    public DocumentType DocumentType => DocumentType.Cad;

    public Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken) =>
        Task.FromResult(Result<InvoiceExtractionResult>.Failure(
            new Error(
                "processor.cad.unsupported",
                $"CAD extraction is not implemented for file '{document.BlobName}'. Convert the CAD file to PDF or image before processing.")));
}
