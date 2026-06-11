using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Application.Services;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class DocumentProcessingOrchestratorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldPersistAndMoveBlob_WhenExtractionSucceeds()
    {
        var blobService = new Mock<IBlobService>();
        var classificationService = new Mock<IDocumentClassificationService>();
        var processorFactory = new Mock<IDocumentProcessorFactory>();
        var validator = new Mock<IInvoiceExtractionValidator>();
        var invoiceRepository = new Mock<IInvoiceRepository>();
        var auditRepository = new Mock<IProcessingAuditRepository>();
        var historyRepository = new Mock<IBlobProcessingHistoryRepository>();
        var clock = new Mock<IClock>();
        var logger = Mock.Of<ILogger<DocumentProcessingOrchestrator>>();
        var processor = new Mock<IDocumentProcessor>();

        var invoice = new InvoiceHeader
        {
            InvoiceNumber = "INV-1",
            VendorName = "Vendor",
            TotalAmount = 100
        };

        blobService.Setup(x => x.DownloadAsync(It.IsAny<BlobPointer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BlobDocument>.Success(new BlobDocument
            {
                BlobName = "invoice.pdf",
                ContainerName = "incoming",
                ContentType = "application/pdf",
                Content = [0x25, 0x50, 0x44, 0x46]
            }));
        classificationService.Setup(x => x.Classify(It.IsAny<BlobDocument>()))
            .Returns(Result<DocumentType>.Success(DocumentType.Pdf));
        processorFactory.Setup(x => x.Resolve(DocumentType.Pdf))
            .Returns(Result<IDocumentProcessor>.Success(processor.Object));
        processor.Setup(x => x.ProcessAsync(It.IsAny<BlobDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InvoiceExtractionResult>.Success(new InvoiceExtractionResult
            {
                IsSuccessful = true,
                DocumentType = DocumentType.Pdf,
                Invoice = invoice
            }));
        validator.Setup(x => x.Validate(It.IsAny<InvoiceHeader>())).Returns(Result.Success());
        invoiceRepository.Setup(x => x.SaveAsync(It.IsAny<InvoiceHeader>(), It.IsAny<CancellationToken>())).ReturnsAsync(42L);
        blobService.Setup(x => x.MoveToProcessedAsync(It.IsAny<BlobPointer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BlobMoveResult>.Success(new BlobMoveResult { DestinationContainer = "processed", DestinationBlobName = "invoice.pdf" }));
        blobService.Setup(x => x.UploadRawResponseAsync(It.IsAny<RawResponseUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        var sut = new DocumentProcessingOrchestrator(
            blobService.Object,
            classificationService.Object,
            processorFactory.Object,
            validator.Object,
            invoiceRepository.Object,
            auditRepository.Object,
            historyRepository.Object,
            clock.Object,
            logger);

        var result = await sut.ProcessAsync(new DocumentProcessingRequest
        {
            BlobName = "invoice.pdf",
            ContainerName = "incoming",
            CorrelationId = "corr-1"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.InvoiceHeaderId.Should().Be(42);
        invoiceRepository.Verify(x => x.SaveAsync(It.IsAny<InvoiceHeader>(), It.IsAny<CancellationToken>()), Times.Once);
        blobService.Verify(x => x.MoveToProcessedAsync(It.IsAny<BlobPointer>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
