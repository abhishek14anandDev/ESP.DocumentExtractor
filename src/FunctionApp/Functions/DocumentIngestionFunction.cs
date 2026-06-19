using System.Text.Json;
using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ESP.DocumentExtractor.FunctionApp.Functions;

public sealed class DocumentIngestionFunction(
    IDocumentProcessingService processingService,
    ILogger<DocumentIngestionFunction> logger)
{
    [Function(nameof(DocumentIngestionFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "documents/process")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        DocumentProcessingRequest? documentRequest;
        try
        {
            documentRequest = await request.ReadFromJsonAsync<DocumentProcessingRequest>(cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Received invalid JSON payload.");
            return new BadRequestObjectResult("Request body contains invalid JSON.");
        }

        if (documentRequest is null)
        {
            return new BadRequestObjectResult("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(documentRequest.BlobName) || string.IsNullOrWhiteSpace(documentRequest.ContainerName))
        {
            return new BadRequestObjectResult("Both BlobName and ContainerName are required.");
        }

        var correlationId = string.IsNullOrWhiteSpace(documentRequest.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : documentRequest.CorrelationId;

        var requestToProcess = new DocumentProcessingRequest
        {
            BlobName = documentRequest.BlobName,
            ContainerName = documentRequest.ContainerName,
            BlobUri = documentRequest.BlobUri,
            EventType = documentRequest.EventType,
            CorrelationId = correlationId,
            ExtractionMode = documentRequest.ExtractionMode
        };

        var result = await processingService.ProcessAsync(requestToProcess, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogError(
                "Document processing failed for blob {BlobName}: {ErrorMessage}",
                requestToProcess.BlobName,
                result.Error.Message);

            return new ObjectResult(new DocumentProcessingResponse
            {
                BlobName = requestToProcess.BlobName,
                ContainerName = requestToProcess.ContainerName,
                CorrelationId = requestToProcess.CorrelationId,
                DocumentType = Domain.Enums.DocumentType.Unsupported,
                ProcessingStatus = Domain.Enums.ProcessingStatus.Failed,
                ErrorMessage = result.Error.Message,
                Duration = TimeSpan.Zero
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        return new OkObjectResult(result.Value);
    }
}
