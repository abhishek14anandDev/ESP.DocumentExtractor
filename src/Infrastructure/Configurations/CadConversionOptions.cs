namespace ESP.DocumentExtractor.Infrastructure.Configurations;

public sealed class CadConversionOptions
{
    public string Ogr2OgrPath { get; init; } = "ogr2ogr";
    public int TimeoutSeconds { get; init; } = 120;
}
