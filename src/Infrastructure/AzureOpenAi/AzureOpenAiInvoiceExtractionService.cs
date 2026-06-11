using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.AzureOpenAi;

public sealed partial class AzureOpenAiInvoiceExtractionService(
    HttpClient httpClient,
    IOptions<AzureOpenAiOptions> options,
    IRetryPolicyExecutor retryPolicyExecutor,
    ILogger<AzureOpenAiInvoiceExtractionService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<Result<InvoiceExtractionResult>> ExtractInvoiceAsync(BlobDocument document, CancellationToken cancellationToken) =>
        retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                try
                {
                    var requestBody = BuildChatCompletionRequest(document, options.Value);
                    using var response = await httpClient.PostAsJsonAsync(
                        BuildRequestUri(options.Value),
                        requestBody,
                        SerializerOptions,
                        token);

                    var rawResponse = await response.Content.ReadAsStringAsync(token);
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogError(
                            "Azure OpenAI invoice extraction failed for {BlobName} with status {StatusCode}: {ResponseBody}",
                            document.BlobName,
                            response.StatusCode,
                            rawResponse);
                        return Result<InvoiceExtractionResult>.Failure(
                            new Error("azure-openai.extract", $"Azure OpenAI returned {(int)response.StatusCode}."));
                    }

                    var completion = JsonSerializer.Deserialize<AzureOpenAiChatCompletionResponse>(rawResponse, SerializerOptions);
                    var content = completion?.Choices.FirstOrDefault()?.Message?.Content;
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return Result<InvoiceExtractionResult>.Failure(
                            new Error("azure-openai.empty", "Azure OpenAI returned an empty invoice payload."));
                    }

                    var extracted = JsonSerializer.Deserialize<AzureOpenAiInvoicePayload>(content, SerializerOptions);
                    if (extracted is null)
                    {
                        return Result<InvoiceExtractionResult>.Failure(
                            new Error("azure-openai.parse", "Azure OpenAI returned an unreadable invoice payload."));
                    }

                    var invoice = MapInvoice(extracted, document);
                    return Result<InvoiceExtractionResult>.Success(new InvoiceExtractionResult
                    {
                        IsSuccessful = true,
                        DocumentType = ResolveDocumentType(document),
                        Invoice = invoice,
                        RawProviderResponse = rawResponse,
                        ProcessorName = nameof(AzureOpenAiInvoiceExtractionService)
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Azure OpenAI extraction failed for {BlobName}.", document.BlobName);
                    return Result<InvoiceExtractionResult>.Failure(new Error("azure-openai.extract", ex.Message));
                }
            },
            "azure-openai-extract",
            cancellationToken);

    private static string BuildRequestUri(AzureOpenAiOptions options) =>
        $"{options.Endpoint.TrimEnd('/')}/openai/deployments/{options.DeploymentName}/chat/completions?api-version={options.ApiVersion}";

    private static object BuildChatCompletionRequest(BlobDocument document, AzureOpenAiOptions options)
    {
        var userContent = BuildUserContent(document);
        return new
        {
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You extract invoice data from business documents. Return only JSON that matches the provided schema. Use null for unknown values. Dates must be yyyy-MM-dd. Do not invent missing fields."
                },
                new
                {
                    role = "user",
                    content = userContent
                }
            },
            temperature = options.Temperature,
            max_tokens = options.MaxTokens,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "invoice_extraction",
                    strict = true,
                    schema = BuildInvoiceSchema()
                }
            }
        };
    }

    private static object BuildUserContent(BlobDocument document)
    {
        if (IsImage(document))
        {
            return new object[]
            {
                new
                {
                    type = "text",
                    text = "Extract the invoice header and line items from this document image."
                },
                new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:{ResolveMimeType(document)};base64,{Convert.ToBase64String(document.Content)}",
                        detail = "high"
                    }
                }
            };
        }

        var extractedText = ExtractStructuredText(document);
        return $"Extract invoice data from the following document content.\n\nDocument name: {document.BlobName}\nContent:\n{extractedText}";
    }

    private static object BuildInvoiceSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[]
        {
            "invoiceNumber", "vendorName", "vendorAddress", "invoiceDate", "dueDate", "currency", "subtotal",
            "taxAmount", "totalAmount", "purchaseOrderNumber", "paymentTerms", "customerName", "confidence", "lineItems"
        },
        properties = new
        {
            invoiceNumber = NullableStringProperty(),
            vendorName = NullableStringProperty(),
            vendorAddress = NullableStringProperty(),
            invoiceDate = NullableStringProperty(),
            dueDate = NullableStringProperty(),
            currency = NullableStringProperty(),
            subtotal = NullableNumberProperty(),
            taxAmount = NullableNumberProperty(),
            totalAmount = NullableNumberProperty(),
            purchaseOrderNumber = NullableStringProperty(),
            paymentTerms = NullableStringProperty(),
            customerName = NullableStringProperty(),
            confidence = NullableNumberProperty(),
            lineItems = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "description", "quantity", "unitPrice", "amount", "tax", "sku", "unit" },
                    properties = new
                    {
                        description = NullableStringProperty(),
                        quantity = NullableNumberProperty(),
                        unitPrice = NullableNumberProperty(),
                        amount = NullableNumberProperty(),
                        tax = NullableNumberProperty(),
                        sku = NullableStringProperty(),
                        unit = NullableStringProperty()
                    }
                }
            }
        }
    };

    private static object NullableStringProperty() => new { type = new[] { "string", "null" } };

    private static object NullableNumberProperty() => new { type = new[] { "number", "null" } };

    private static InvoiceHeader MapInvoice(AzureOpenAiInvoicePayload payload, BlobDocument document)
    {
        var documentType = ResolveDocumentType(document);
        var invoice = new InvoiceHeader
        {
            InvoiceNumber = payload.InvoiceNumber ?? string.Empty,
            VendorName = payload.VendorName ?? string.Empty,
            VendorAddress = payload.VendorAddress,
            InvoiceDate = ParseDate(payload.InvoiceDate),
            DueDate = ParseDate(payload.DueDate),
            Currency = payload.Currency,
            Subtotal = ToDecimal(payload.Subtotal),
            TaxAmount = ToDecimal(payload.TaxAmount),
            TotalAmount = ToDecimal(payload.TotalAmount),
            PurchaseOrderNumber = payload.PurchaseOrderNumber,
            PaymentTerms = payload.PaymentTerms,
            CustomerName = payload.CustomerName,
            InvoiceConfidenceScore = ToConfidence(payload.Confidence),
            SourceFileName = document.BlobName,
            SourceFileType = documentType.ToString(),
            ProcessingStatus = ProcessingStatus.Extracted
        };

        foreach (var item in payload.LineItems)
        {
            if (string.IsNullOrWhiteSpace(item.Description) &&
                item.Quantity is null &&
                item.UnitPrice is null &&
                item.Amount is null &&
                item.Tax is null &&
                string.IsNullOrWhiteSpace(item.Sku) &&
                string.IsNullOrWhiteSpace(item.Unit))
            {
                continue;
            }

            invoice.LineItems.Add(new InvoiceLineItem
            {
                Description = item.Description ?? string.Empty,
                Quantity = ToDecimal(item.Quantity),
                UnitPrice = ToDecimal(item.UnitPrice),
                Amount = ToDecimal(item.Amount),
                Tax = ToDecimal(item.Tax),
                Sku = item.Sku,
                Unit = item.Unit
            });
        }

        return invoice;
    }

    private static string ExtractStructuredText(BlobDocument document)
    {
        var extension = Path.GetExtension(document.BlobName);
        return extension.ToLowerInvariant() switch
        {
            ".csv" => ReadText(document.Content),
            ".xlsx" or ".xlsm" => ExtractWorkbookText(document.Content),
            ".docx" => ExtractDocxText(document.Content),
            ".doc" => throw new InvalidOperationException("Legacy .doc files are not supported by the Azure OpenAI extraction path."),
            ".pdf" => ExtractPdfText(document.Content, document.BlobName),
            _ => ReadText(document.Content)
        };
    }

    private static string ReadText(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ExtractWorkbookText(byte[] content)
    {
        using var workbook = new XLWorkbook(new MemoryStream(content));
        var builder = new StringBuilder();

        foreach (var worksheet in workbook.Worksheets)
        {
            builder.AppendLine($"Worksheet: {worksheet.Name}");
            var rows = worksheet.RangeUsed()?.RowsUsed().ToList() ?? [];
            foreach (var row in rows)
            {
                var values = row.CellsUsed().Select(cell => cell.GetString().Trim());
                builder.AppendLine(string.Join('\t', values));
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ExtractDocxText(byte[] content)
    {
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("Word document content could not be located.");

        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        var paragraphs = xml
            .Descendants()
            .Where(element => element.Name.LocalName == "p")
            .Select(paragraph => string.Concat(
                paragraph.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value)))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine, paragraphs);
    }

    private static string ExtractPdfText(byte[] content, string blobName)
    {
        var raw = Encoding.Latin1.GetString(content);
        var collected = new StringBuilder();

        foreach (Match match in PdfTextTokenRegex().Matches(raw))
        {
            collected.AppendLine(DecodePdfLiteral(match.Groups["text"].Value));
        }

        if (collected.Length > 0)
        {
            return collected.ToString();
        }

        foreach (Match match in FlateStreamRegex().Matches(raw))
        {
            var decoded = TryInflate(match.Groups["stream"].Value);
            if (string.IsNullOrWhiteSpace(decoded))
            {
                continue;
            }

            foreach (Match textMatch in PdfTextTokenRegex().Matches(decoded))
            {
                collected.AppendLine(DecodePdfLiteral(textMatch.Groups["text"].Value));
            }
        }

        if (collected.Length == 0)
        {
            throw new InvalidOperationException($"PDF text extraction failed for '{blobName}'. Azure OpenAI requires readable text or image content.");
        }

        return collected.ToString();
    }

    private static string? TryInflate(string streamContent)
    {
        try
        {
            var start = Encoding.Latin1.GetBytes(streamContent);
            using var input = new MemoryStream(start);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.Latin1);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string DecodePdfLiteral(string value) =>
        value
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static bool IsImage(BlobDocument document) =>
        document.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(document.BlobName).Equals(".bmp", StringComparison.OrdinalIgnoreCase);

    private static string ResolveMimeType(BlobDocument document) =>
        !string.IsNullOrWhiteSpace(document.ContentType) && !document.ContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? document.ContentType
            : Path.GetExtension(document.BlobName).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                ".tif" or ".tiff" => "image/tiff",
                _ => "application/octet-stream"
            };

    private static DocumentType ResolveDocumentType(BlobDocument document)
    {
        var extension = Path.GetExtension(document.BlobName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => DocumentType.Pdf,
            ".doc" or ".docx" => DocumentType.Word,
            ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" => DocumentType.Image,
            ".bmp" => DocumentType.Screenshot,
            ".csv" => DocumentType.Csv,
            ".xlsx" or ".xlsm" or ".xls" => DocumentType.Excel,
            _ when document.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => DocumentType.Image,
            _ => DocumentType.Unsupported
        };
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var parsed) ? parsed : null;

    private static decimal? ToDecimal(double? value) =>
        value.HasValue ? Convert.ToDecimal(value.Value) : null;

    private static decimal ToConfidence(double? value)
    {
        if (!value.HasValue)
        {
            return 0;
        }

        var confidence = Convert.ToDecimal(value.Value);
        return confidence > 1 ? confidence / 100 : confidence;
    }

    [GeneratedRegex(@"\((?<text>(?:\\.|[^\\)])*)\)\s*Tj", RegexOptions.Compiled)]
    private static partial Regex PdfTextTokenRegex();

    [GeneratedRegex(@"stream\r?\n(?<stream>.*?)\r?\nendstream", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex FlateStreamRegex();

    private sealed class AzureOpenAiChatCompletionResponse
    {
        public List<Choice> Choices { get; init; } = [];

        public sealed class Choice
        {
            public Message? Message { get; init; }
        }

        public sealed class Message
        {
            public string? Content { get; init; }
        }
    }

    private sealed class AzureOpenAiInvoicePayload
    {
        public string? InvoiceNumber { get; init; }
        public string? VendorName { get; init; }
        public string? VendorAddress { get; init; }
        public string? InvoiceDate { get; init; }
        public string? DueDate { get; init; }
        public string? Currency { get; init; }
        public double? Subtotal { get; init; }
        public double? TaxAmount { get; init; }
        public double? TotalAmount { get; init; }
        public string? PurchaseOrderNumber { get; init; }
        public string? PaymentTerms { get; init; }
        public string? CustomerName { get; init; }
        public double? Confidence { get; init; }
        public List<AzureOpenAiInvoiceLineItem> LineItems { get; init; } = [];
    }

    private sealed class AzureOpenAiInvoiceLineItem
    {
        public string? Description { get; init; }
        public double? Quantity { get; init; }
        public double? UnitPrice { get; init; }
        public double? Amount { get; init; }
        public double? Tax { get; init; }
        public string? Sku { get; init; }
        public string? Unit { get; init; }
    }
}
