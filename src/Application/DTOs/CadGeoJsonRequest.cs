namespace ESP.DocumentExtractor.Application.DTOs;

public sealed class CadGeoJsonRequest
{
    public string? FilePath { get; init; }
    public string? CorrelationId { get; init; }
}
