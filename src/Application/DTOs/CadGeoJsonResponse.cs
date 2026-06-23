namespace ESP.DocumentExtractor.Application.DTOs;

public sealed class CadGeoJsonResponse
{
    public required string CorrelationId { get; init; }
    public required string FileName { get; init; }
    public required string GeoJson { get; init; }
}
