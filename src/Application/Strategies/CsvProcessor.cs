using System.Globalization;
using CsvHelper;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Strategies;

public sealed class CsvProcessor : ICsvProcessor
{
    public DocumentType DocumentType => DocumentType.Csv;

    public async Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(document.Content);
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var records = new List<dynamic>();
            await foreach (var record in csv.GetRecordsAsync<dynamic>(cancellationToken))
            {
                records.Add(record);
            }

            if (records.Count == 0)
            {
                return Result<InvoiceExtractionResult>.Failure(new Error("csv.empty", "CSV file did not contain invoice rows."));
            }

            var first = ToDictionary(records[0]);
            var invoice = new InvoiceHeader
            {
                InvoiceNumber = Read(first, "InvoiceNumber"),
                VendorName = Read(first, "VendorName"),
                VendorAddress = Read(first, "VendorAddress"),
                InvoiceDate = ParseDate(Read(first, "InvoiceDate")),
                DueDate = ParseDate(Read(first, "DueDate")),
                Currency = Read(first, "Currency"),
                Subtotal = ParseDecimal(Read(first, "Subtotal")),
                TaxAmount = ParseDecimal(Read(first, "TaxAmount")),
                TotalAmount = ParseDecimal(Read(first, "TotalAmount")),
                PurchaseOrderNumber = Read(first, "PurchaseOrderNumber"),
                PaymentTerms = Read(first, "PaymentTerms"),
                CustomerName = Read(first, "CustomerName"),
                InvoiceConfidenceScore = 0.95m,
                SourceFileName = document.BlobName,
                SourceFileType = DocumentType.ToString(),
                ProcessingStatus = ProcessingStatus.Extracted
            };

            foreach (var record in records)
            {
                var row = ToDictionary(record);
                invoice.LineItems.Add(new InvoiceLineItem
                {
                    Description = Read(row, "Description"),
                    Quantity = ParseDecimal(Read(row, "Quantity")),
                    UnitPrice = ParseDecimal(Read(row, "UnitPrice")),
                    Amount = ParseDecimal(Read(row, "Amount")),
                    Tax = ParseDecimal(Read(row, "Tax")),
                    Sku = Read(row, "SKU"),
                    Unit = Read(row, "Unit")
                });
            }

            return Result<InvoiceExtractionResult>.Success(new InvoiceExtractionResult
            {
                IsSuccessful = true,
                DocumentType = DocumentType,
                Invoice = invoice,
                ProcessorName = nameof(CsvProcessor)
            });
        }
        catch (Exception ex)
        {
            return Result<InvoiceExtractionResult>.Failure(new Error("csv.extract", ex.Message));
        }
    }

    private static IDictionary<string, string?> ToDictionary(dynamic value) =>
        ((IDictionary<string, object>)value).ToDictionary(x => x.Key, x => x.Value?.ToString(), StringComparer.OrdinalIgnoreCase);

    private static string Read(IDictionary<string, string?> row, string key) => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static DateOnly? ParseDate(string value) => DateOnly.TryParse(value, out var parsed) ? parsed : null;

    private static decimal? ParseDecimal(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
