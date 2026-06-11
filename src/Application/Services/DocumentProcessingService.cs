using System.Diagnostics;
using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.ResultPattern;
using Microsoft.Extensions.Logging;

namespace ESP.DocumentExtractor.Application.Services;

public sealed class DocumentProcessingService(
    IBlobService blobService,
    IDocumentClassificationService classificationService,
    IDocumentProcessorFactory processorFactory,
    IInvoiceExtractionValidator invoiceValidator,
    IInvoiceRepository invoiceRepository,
    IProcessingAuditRepository auditRepository,
    IBlobProcessingHistoryRepository historyRepository,
    IClock clock,
    ILogger<DocumentProcessingService> logger) : IDocumentProcessingService
{
    public async Task<Result<DocumentProcessingResponse>> ProcessAsync(DocumentProcessingRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var pointer = new Domain.Models.BlobPointer
        {
            BlobName = request.BlobName,
            ContainerName = request.ContainerName,
            BlobUri = request.BlobUri,
            CorrelationId = request.CorrelationId
        };

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = request.CorrelationId,
            ["BlobName"] = request.BlobName,
            ["ContainerName"] = request.ContainerName
        });

        logger.LogInformation("Starting document processing workflow.");

        var downloadResult = await blobService.DownloadAsync(pointer, cancellationToken);
        if (downloadResult.IsFailure)
        {
            return await HandleFailureAsync(request, DocumentType.Unsupported, ProcessingStatus.Failed, downloadResult.Error.Message, stopwatch.Elapsed, cancellationToken);
        }

        var downloadedDocument = downloadResult.Value;
        var document = new Domain.Models.BlobDocument
        {
            BlobName = downloadedDocument.BlobName, 
            ContainerName = downloadedDocument.ContainerName,
            ContentType = downloadedDocument.ContentType,
            Content = downloadedDocument.Content,
            ETag = downloadedDocument.ETag,
            CorrelationId = downloadedDocument.CorrelationId,
            ExtractionMode = request.ExtractionMode,
            Metadata = downloadedDocument.Metadata
        };
        var classificationResult = classificationService.Classify(document);
        if (classificationResult.IsFailure)
        {
            return await HandleFailureAsync(request, DocumentType.Unsupported, ProcessingStatus.Failed, classificationResult.Error.Message, stopwatch.Elapsed, cancellationToken);
        }

        var documentType = classificationResult.Value;
        logger.LogInformation("Document classified as {DocumentType}.", documentType);

        var processorResult = processorFactory.Resolve(documentType);
        if (processorResult.IsFailure)
        {
            return await HandleFailureAsync(request, documentType, ProcessingStatus.Failed, processorResult.Error.Message, stopwatch.Elapsed, cancellationToken);
        }

        var extractionResult = await processorResult.Value.ProcessAsync(document, cancellationToken);
        if (extractionResult.IsFailure)
        {
            return await HandleFailureAsync(request, documentType, ProcessingStatus.Rejected, extractionResult.Error.Message, stopwatch.Elapsed, cancellationToken);
        }

        var invoiceResult = extractionResult.Value;
        var validationResult = invoiceValidator.Validate(invoiceResult.Invoice);
        if (validationResult.IsFailure)
        {
            logger.LogWarning("Invoice validation failed: {ValidationError}", validationResult.Error.Message);
            return await HandleFailureAsync(request, documentType, ProcessingStatus.Rejected, validationResult.Error.Message, stopwatch.Elapsed, cancellationToken);
        }

        invoiceResult.Invoice.ProcessingStatus = ProcessingStatus.Validated;
        invoiceResult.Invoice.ProcessingDate = clock.UtcNow;
        invoiceResult.Invoice.SourceFileName = document.BlobName;
        invoiceResult.Invoice.SourceFileType = documentType.ToString();

        var invoiceHeaderId = await invoiceRepository.SaveAsync(invoiceResult.Invoice, cancellationToken);
        logger.LogInformation("Invoice persisted with ID {InvoiceHeaderId}.", invoiceHeaderId);

        if (!string.IsNullOrWhiteSpace(invoiceResult.RawProviderResponse))
        {
            await blobService.UploadRawResponseAsync(
                new RawResponseUploadRequest
                {
                    BlobName = document.BlobName,
                    CorrelationId = request.CorrelationId,
                    Content = invoiceResult.RawProviderResponse
                },
                cancellationToken);
        }

        await blobService.MoveToProcessedAsync(pointer, cancellationToken);

        await auditRepository.SaveAsync(
            new DocumentProcessingAudit
            {
                BlobName = request.BlobName,
                ContainerName = request.ContainerName,
                CorrelationId = request.CorrelationId,
                DocumentType = documentType,
                ProcessingStatus = ProcessingStatus.Processed,
                InvoiceHeaderId = invoiceHeaderId,
                Message = "Processing completed successfully.",
                ProcessingDuration = stopwatch.Elapsed,
                ProcessedOn = clock.UtcNow
            },
            cancellationToken);

        await historyRepository.SaveAsync(
            new BlobProcessingHistory
            {
                BlobName = request.BlobName,
                SourceContainer = request.ContainerName,
                DestinationContainer = Domain.Constants.StorageContainers.Processed,
                CorrelationId = request.CorrelationId,
                DocumentType = documentType,
                ProcessingStatus = ProcessingStatus.Processed,
                ProcessedOn = clock.UtcNow
            },
            cancellationToken);

        stopwatch.Stop();

        return Result<DocumentProcessingResponse>.Success(new DocumentProcessingResponse
        {
            BlobName = request.BlobName,
            ContainerName = request.ContainerName,
            CorrelationId = request.CorrelationId,
            DocumentType = documentType,
            ProcessingStatus = ProcessingStatus.Processed,
            InvoiceHeaderId = invoiceHeaderId,
            Duration = stopwatch.Elapsed
        });
    }

    private async Task<Result<DocumentProcessingResponse>> HandleFailureAsync(
        DocumentProcessingRequest request,
        DocumentType documentType,
        ProcessingStatus status,
        string errorMessage,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        logger.LogError("Document processing failed with status {Status}: {ErrorMessage}", status, errorMessage);

        var pointer = new Domain.Models.BlobPointer
        {
            BlobName = request.BlobName,
            ContainerName = request.ContainerName,
            BlobUri = request.BlobUri,
            CorrelationId = request.CorrelationId
        };

        await blobService.MoveToRejectedAsync(pointer, errorMessage, cancellationToken);

        await auditRepository.SaveAsync(
            new DocumentProcessingAudit
            {
                BlobName = request.BlobName,
                ContainerName = request.ContainerName,
                CorrelationId = request.CorrelationId,
                DocumentType = documentType,
                ProcessingStatus = status,
                ErrorMessage = errorMessage,
                Message = "Processing failed.",
                ProcessingDuration = elapsed,
                ProcessedOn = clock.UtcNow
            },
            cancellationToken);

        await historyRepository.SaveAsync(
            new BlobProcessingHistory
            {
                BlobName = request.BlobName,
                SourceContainer = request.ContainerName,
                DestinationContainer = Domain.Constants.StorageContainers.Rejected,
                CorrelationId = request.CorrelationId,
                DocumentType = documentType,
                ProcessingStatus = status,
                ErrorMessage = errorMessage,
                ProcessedOn = clock.UtcNow
            },
            cancellationToken);

        return Result<DocumentProcessingResponse>.Failure(new Error("processing.failed", errorMessage));
    }
}
