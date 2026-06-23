using ESP.DocumentExtractor.Application.Strategies;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using FluentAssertions;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class CadProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldFail_WhenCadExtractionIsNotImplemented()
    {
        var sut = new CadProcessor();

        var result = await sut.ProcessAsync(new BlobDocument
        {
            BlobName = "drawing.dwg",
            ContainerName = "incoming",
            ContentType = "application/octet-stream",
            Content = [0x01],
            CorrelationId = "corr-1",
            ExtractionMode = "normal"
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("processor.cad.unsupported");
        result.Error.Message.Should().Contain("drawing.dwg");
        sut.DocumentType.Should().Be(DocumentType.Cad);
    }
}
