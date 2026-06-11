using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.AzureOpenAi;
using ESP.DocumentExtractor.Infrastructure.DocumentIntelligence;

namespace ESP.DocumentExtractor.Infrastructure.Services;

public sealed class ConfigurableInvoiceExtractionService(
    AzureOpenAiInvoiceExtractionService azureOpenAiInvoiceExtractionService,
    DocumentIntelligenceInvoiceExtractionService documentIntelligenceInvoiceExtractionService)
    : IInvoiceExtractionService
{
    public Task<Result<InvoiceExtractionResult>> ExtractInvoiceAsync(BlobDocument document, CancellationToken cancellationToken)
    {
        var useAi = document.ExtractionMode.Equals("ai", StringComparison.OrdinalIgnoreCase) ||
                    document.ExtractionMode.Equals("azureopenai", StringComparison.OrdinalIgnoreCase) ||
                    document.ExtractionMode.Equals("openai", StringComparison.OrdinalIgnoreCase);

        return useAi
            ? azureOpenAiInvoiceExtractionService.ExtractInvoiceAsync(document, cancellationToken)
            : documentIntelligenceInvoiceExtractionService.ExtractInvoiceAsync(document, cancellationToken);
    }
}
