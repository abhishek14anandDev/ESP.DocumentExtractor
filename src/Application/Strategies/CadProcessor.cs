using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Strategies;

public sealed class CadProcessor : ICadProcessor
{
    public DocumentType DocumentType => DocumentType.Cad;

    public Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken) =>
        Task.FromResult(Result<InvoiceExtractionResult>.Success(new InvoiceExtractionResult
        {
            IsSuccessful = true,
            DocumentType = DocumentType,
            Invoice = new InvoiceHeader
            {
                InvoiceNumber = Path.GetFileNameWithoutExtension(document.BlobName),
                VendorName = "CAD-UNPARSED",
                ErrorMessage = "CAD extraction engine not configured. Document accepted for future extensibility.",
                SourceFileName = document.BlobName,
                SourceFileType = DocumentType.ToString(),
                ProcessingStatus = ProcessingStatus.Extracted
            },
            ValidationErrors = ["CAD extraction is not yet implemented."],
            ProcessorName = nameof(CadProcessor)
        }));
}
