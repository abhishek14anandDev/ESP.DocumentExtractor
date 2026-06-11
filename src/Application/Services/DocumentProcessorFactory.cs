using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Services;

public sealed class DocumentProcessorFactory(IEnumerable<IDocumentProcessor> processors) : IDocumentProcessorFactory
{
    private readonly IReadOnlyDictionary<DocumentType, IDocumentProcessor> _processors =
        processors.ToDictionary(x => x.DocumentType, x => x);

    public Result<IDocumentProcessor> Resolve(DocumentType documentType)
    {
        if (documentType == DocumentType.Screenshot && _processors.TryGetValue(DocumentType.Image, out var imageProcessor))
        {
            return Result<IDocumentProcessor>.Success(imageProcessor);
        }

        if (_processors.TryGetValue(documentType, out var processor))
        {
            return Result<IDocumentProcessor>.Success(processor);
        }

        return _processors.TryGetValue(DocumentType.Unsupported, out var fallback)
            ? Result<IDocumentProcessor>.Success(fallback)
            : Result<IDocumentProcessor>.Failure(new Error("processor.missing", $"No processor registered for document type '{documentType}'."));
    }
}
