using Azure;
using Azure.AI.DocumentIntelligence;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.DocumentIntelligence;

public sealed class DocumentIntelligenceInvoiceExtractionService(
    IOptions<DocumentIntelligenceOptions> options,
    IRetryPolicyExecutor retryPolicyExecutor,
    ILogger<DocumentIntelligenceInvoiceExtractionService> logger)
{
    private readonly DocumentIntelligenceClient _client = new(
        new Uri(options.Value.Endpoint),
        new AzureKeyCredential(options.Value.ApiKey));

    public Task<Result<InvoiceExtractionResult>> ExtractInvoiceAsync(BlobDocument document, CancellationToken cancellationToken) =>
        retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                try
                {
                    var operation = await _client.AnalyzeDocumentAsync(
                        WaitUntil.Completed,
                        options.Value.ModelId,
                        BinaryData.FromBytes(document.Content),
                        cancellationToken: token);

                    var result = DocumentIntelligenceMapper.Map(
                        operation.Value,
                        document.BlobName,
                        ResolveDocumentType(document.ContentType));

                    return Result<InvoiceExtractionResult>.Success(new InvoiceExtractionResult
                    {
                        IsSuccessful = result.IsSuccessful,
                        DocumentType = result.DocumentType,
                        Invoice = result.Invoice,
                        ValidationErrors = result.ValidationErrors,
                        ErrorMessage = result.ErrorMessage,
                        RawProviderResponse = operation.GetRawResponse().Content.ToString(),
                        ProcessorName = nameof(DocumentIntelligenceInvoiceExtractionService)
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Document intelligence extraction failed for {BlobName}.", document.BlobName);
                    return Result<InvoiceExtractionResult>.Failure(new Error("document-intelligence.extract", ex.Message));
                }
            },
            "document-intelligence-extract",
            cancellationToken);

    private static DocumentType ResolveDocumentType(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? DocumentType.Image
            : contentType.Contains("word", StringComparison.OrdinalIgnoreCase)
                ? DocumentType.Word
                : DocumentType.Pdf;
}
