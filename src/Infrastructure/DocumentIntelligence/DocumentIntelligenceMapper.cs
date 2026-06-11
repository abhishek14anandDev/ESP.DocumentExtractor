using Azure.AI.DocumentIntelligence;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;

namespace ESP.DocumentExtractor.Infrastructure.DocumentIntelligence;

internal static class DocumentIntelligenceMapper
{
    public static InvoiceExtractionResult Map(AnalyzeResult analyzeResult, string fileName, DocumentType documentType)
    {
        var document = analyzeResult.Documents.FirstOrDefault();
        var invoice = new InvoiceHeader
        {
            InvoiceNumber = GetString(document, "InvoiceId"),
            VendorName = GetString(document, "VendorName"),
            VendorAddress = GetAddress(document, "VendorAddress"),
            InvoiceDate = GetDate(document, "InvoiceDate"),
            DueDate = GetDate(document, "DueDate"),
            Currency = GetCurrency(document, "InvoiceTotal"),
            Subtotal = GetDecimal(document, "SubTotal"),
            TaxAmount = GetDecimal(document, "TotalTax"),
            TotalAmount = GetDecimal(document, "InvoiceTotal"),
            PurchaseOrderNumber = GetString(document, "PurchaseOrder"),
            PaymentTerms = GetString(document, "PaymentTerm"),
            CustomerName = GetString(document, "CustomerName"),
            InvoiceConfidenceScore = (decimal)(document?.Fields.Values.DefaultIfEmpty().Average(x => x?.Confidence ?? 0) ?? 0),
            SourceFileName = fileName,
            SourceFileType = documentType.ToString(),
            ProcessingStatus = ProcessingStatus.Extracted
        };

        if (document?.Fields.TryGetValue("Items", out var itemsField) == true && itemsField.FieldType == DocumentFieldType.List)
        {
            foreach (var item in itemsField.ValueList)
            {
                if (item.FieldType != DocumentFieldType.Dictionary)
                {
                    continue;
                }

                invoice.LineItems.Add(new InvoiceLineItem
                {
                    Description = GetString(item.ValueDictionary, "Description"),
                    Quantity = GetDecimal(item.ValueDictionary, "Quantity"),
                    UnitPrice = GetDecimal(item.ValueDictionary, "UnitPrice"),
                    Amount = GetDecimal(item.ValueDictionary, "Amount"),
                    Tax = GetDecimal(item.ValueDictionary, "Tax"),
                    Sku = GetString(item.ValueDictionary, "ProductCode"),
                    Unit = GetString(item.ValueDictionary, "Unit")
                });
            }
        }

        return new InvoiceExtractionResult
        {
            IsSuccessful = true,
            DocumentType = documentType,
            Invoice = invoice
        };
    }

    private static string GetString(AnalyzedDocument? document, string key) =>
        document?.Fields.TryGetValue(key, out var field) == true
            ? field.Content ?? string.Empty
            : string.Empty;

    private static string? GetAddress(AnalyzedDocument? document, string key) =>
        document?.Fields.TryGetValue(key, out var field) == true
            ? field.Content
            : null;

    private static DateOnly? GetDate(AnalyzedDocument? document, string key) =>
        document?.Fields.TryGetValue(key, out var field) == true && field.ValueDate is { } date
            ? DateOnly.FromDateTime(date.DateTime)
            : null;

    private static decimal? GetDecimal(AnalyzedDocument? document, string key) =>
        document?.Fields.TryGetValue(key, out var field) == true
            ? GetDecimal(field)
            : null;

    private static decimal? GetDecimal(IReadOnlyDictionary<string, DocumentField> dictionary, string key) =>
        dictionary.TryGetValue(key, out var field)
            ? GetDecimal(field)
            : null;

    private static decimal? GetDecimal(DocumentField field)
    {
        if (field.ValueCurrency is { Amount: var amount })
        {
            return (decimal)amount;
        }

        if (field.ValueDouble is { } number)
        {
            return Convert.ToDecimal(number);
        }

        return decimal.TryParse(field.Content, out var parsed) ? parsed : null;
    }

    private static string? GetCurrency(AnalyzedDocument? document, string key) =>
        document?.Fields.TryGetValue(key, out var field) == true
            ? field.ValueCurrency?.CurrencySymbol ?? field.ValueCurrency?.CurrencyCode
            : null;

    private static string? GetString(IReadOnlyDictionary<string, DocumentField> dictionary, string key) =>
        dictionary.TryGetValue(key, out var field)
            ? field.Content
            : null;
}
