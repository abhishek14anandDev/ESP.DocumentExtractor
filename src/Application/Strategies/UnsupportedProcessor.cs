using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Strategies;

public sealed class UnsupportedProcessor : IUnsupportedProcessor
{
    public DocumentType DocumentType => DocumentType.Unsupported;

    public Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken) =>
        Task.FromResult(Result<InvoiceExtractionResult>.Failure(new Error("processor.unsupported", $"File '{document.BlobName}' is unsupported.")));
}
