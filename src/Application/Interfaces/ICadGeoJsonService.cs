using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface ICadGeoJsonService
{
    Task<Result<CadGeoJsonResponse>> ConvertAsync(
        string fileName,
        Stream content,
        string correlationId,
        CancellationToken cancellationToken);
}
