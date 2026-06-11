using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IDocumentProcessorFactory
{
    Result<IDocumentProcessor> Resolve(DocumentType documentType);
}
