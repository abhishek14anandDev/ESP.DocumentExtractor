using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IDocumentProcessingService
{
    Task<Result<DocumentProcessingResponse>> ProcessAsync(DocumentProcessingRequest request, CancellationToken cancellationToken);
}
