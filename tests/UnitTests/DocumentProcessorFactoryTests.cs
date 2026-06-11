using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Application.Services;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;
using FluentAssertions;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class DocumentProcessorFactoryTests
{
    [Fact]
    public void Resolve_ShouldReturnImageProcessorForScreenshot()
    {
        var imageProcessor = new FakeProcessor(DocumentType.Image);
        var unsupportedProcessor = new FakeProcessor(DocumentType.Unsupported);
        var sut = new DocumentProcessorFactory([imageProcessor, unsupportedProcessor]);

        var result = sut.Resolve(DocumentType.Screenshot);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(imageProcessor);
    }

    private sealed class FakeProcessor(DocumentType documentType) : IDocumentProcessor
    {
        public DocumentType DocumentType => documentType;

        public Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken) =>
            Task.FromResult(Result<InvoiceExtractionResult>.Failure(new Error("fake", "Not executed in this test.")));
    }
}
