using ESP.DocumentExtractor.Application.Services;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using FluentAssertions;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class DocumentClassificationServiceTests
{
    private readonly DocumentClassificationService _sut = new();

    [Theory]
    [InlineData("invoice.pdf", "application/pdf", DocumentType.Pdf)]
    [InlineData("invoice.csv", "text/csv", DocumentType.Csv)]
    [InlineData("invoice.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DocumentType.Excel)]
    [InlineData("image.png", "image/png", DocumentType.Image)]
    [InlineData("drawing.dwg", "application/octet-stream", DocumentType.Cad)]
    public void Classify_ShouldDetectByExtension(string fileName, string contentType, DocumentType expected)
    {
        var result = _sut.Classify(new BlobDocument
        {
            BlobName = fileName,
            ContainerName = "incoming",
            ContentType = contentType,
            Content = [1, 2, 3]
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public void Classify_ShouldFallbackToSignature()
    {
        var result = _sut.Classify(new BlobDocument
        {
            BlobName = "unknown.bin",
            ContainerName = "incoming",
            ContentType = "application/octet-stream",
            Content = [0x25, 0x50, 0x44, 0x46]
        });

        result.Value.Should().Be(DocumentType.Pdf);
    }
}
