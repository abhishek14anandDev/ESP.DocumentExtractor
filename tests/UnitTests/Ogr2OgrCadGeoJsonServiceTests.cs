using ESP.DocumentExtractor.Infrastructure.Configurations;
using ESP.DocumentExtractor.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class Ogr2OgrCadGeoJsonServiceTests
{
    [Fact]
    public async Task ConvertAsync_ShouldRejectUnsupportedFileExtension()
    {
        var sut = CreateService();
        await using var stream = new MemoryStream([0x01]);

        var result = await sut.ConvertAsync("drawing.pdf", stream, "corr-1", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("cad.file_type.unsupported");
    }

    [Fact]
    public async Task ConvertAsync_ShouldFail_WhenConverterIsNotFound()
    {
        var sut = CreateService(new CadConversionOptions
        {
            Ogr2OgrPath = "missing-ogr2ogr-for-test",
            TimeoutSeconds = 1
        });
        await using var stream = new MemoryStream([0x01]);

        var result = await sut.ConvertAsync("drawing.dwg", stream, "corr-1", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("cad.converter.not_found");
    }

    [Fact]
    public async Task ConvertAsync_ShouldReturnGeoJson_WhenConverterSucceeds()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"fake-ogr2ogr-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            #!/bin/sh
            printf '{"type":"FeatureCollection","features":[]}' > "$3"
            exit 0
            """);
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var sut = CreateService(new CadConversionOptions
            {
                Ogr2OgrPath = scriptPath,
                TimeoutSeconds = 5
            });
            await using var stream = new MemoryStream([0x01]);

            var result = await sut.ConvertAsync("drawing.dwg", stream, "corr-1", CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.CorrelationId.Should().Be("corr-1");
            result.Value.FileName.Should().Be("drawing.dwg");
            result.Value.GeoJson.Should().Be("""{"type":"FeatureCollection","features":[]}""");
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    private static Ogr2OgrCadGeoJsonService CreateService(CadConversionOptions? options = null) =>
        new(
            Options.Create(options ?? new CadConversionOptions()),
            NullLogger<Ogr2OgrCadGeoJsonService>.Instance);
}
