using Azure.Storage.Blobs;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Application.Services;
using ESP.DocumentExtractor.Application.Validators;
using ESP.DocumentExtractor.Infrastructure.AzureOpenAi;
using ESP.DocumentExtractor.Infrastructure.Blob;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using ESP.DocumentExtractor.Infrastructure.Data;
using ESP.DocumentExtractor.Infrastructure.DocumentIntelligence;
using ESP.DocumentExtractor.Infrastructure.Repositories;
using ESP.DocumentExtractor.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentExtractor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BlobOptions>(configuration.GetSection("Blob"));
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.Configure<SqlOptions>(configuration.GetSection("Sql"));
        services.Configure<AzureOpenAiOptions>(configuration.GetSection("AzureOpenAi"));
        services.Configure<DocumentIntelligenceOptions>(configuration.GetSection("DocumentIntelligence"));
        services.Configure<RetryOptions>(configuration.GetSection("Retry"));
        services.Configure<CadConversionOptions>(configuration.GetSection("CadConversion"));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
            return !string.IsNullOrWhiteSpace(options.ConnectionString)
                ? new BlobServiceClient(options.ConnectionString)
                : new BlobServiceClient(options.ServiceUri!, new Azure.Identity.DefaultAzureCredential());
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IRetryPolicyExecutor, RetryPolicyExecutor>();
        services.AddScoped<IDocumentClassificationService, DocumentClassificationService>();
        services.AddScoped<IDocumentProcessorFactory, DocumentProcessorFactory>();
        services.AddScoped<IInvoiceExtractionValidator, InvoiceExtractionValidator>();
        services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
        services.AddScoped<ICadGeoJsonService, Ogr2OgrCadGeoJsonService>();
        services.AddScoped<IBlobService, BlobService>();
        services.AddHttpClient<AzureOpenAiInvoiceExtractionService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AzureOpenAiOptions>>().Value;
            client.DefaultRequestHeaders.Remove("api-key");
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("api-key", options.ApiKey);
            }
        });
        services.AddScoped<DocumentIntelligenceInvoiceExtractionService>();
        services.AddScoped<IInvoiceExtractionService, ConfigurableInvoiceExtractionService>();
        services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IProcessingAuditRepository, ProcessingAuditRepository>();
        services.AddScoped<IBlobProcessingHistoryRepository, BlobProcessingHistoryRepository>();

        services.AddProcessors();

        return services;
    }

    private static IServiceCollection AddProcessors(this IServiceCollection services)
    {
        services.AddScoped<Application.Interfaces.IPdfProcessor, Application.Strategies.PdfProcessor>();
        services.AddScoped<Application.Interfaces.IImageProcessor, Application.Strategies.ImageProcessor>();
        services.AddScoped<Application.Interfaces.ICsvProcessor, Application.Strategies.CsvProcessor>();
        services.AddScoped<Application.Interfaces.IExcelProcessor, Application.Strategies.ExcelProcessor>();
        services.AddScoped<Application.Interfaces.IWordProcessor, Application.Strategies.WordProcessor>();
        services.AddScoped<Application.Interfaces.ICadProcessor, Application.Strategies.CadProcessor>();
        services.AddScoped<Application.Interfaces.IUnsupportedProcessor, Application.Strategies.UnsupportedProcessor>();
        services.AddScoped<IDocumentProcessor, Application.Strategies.PdfProcessor>();
        services.AddScoped<IDocumentProcessor, Application.Strategies.ImageProcessor>();
        services.AddScoped<IDocumentProcessor, Application.Strategies.CsvProcessor>();
        services.AddScoped<IDocumentProcessor, Application.Strategies.ExcelProcessor>();
        services.AddScoped<IDocumentProcessor, Application.Strategies.WordProcessor>();
        services.AddScoped<IDocumentProcessor, Application.Strategies.CadProcessor>();
        services.AddScoped<IDocumentProcessor, Application.Strategies.UnsupportedProcessor>();
        return services;
    }
}
