using ClosedXML.Excel;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Enums;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Strategies;

public sealed class ExcelProcessor : IExcelProcessor
{
    public DocumentType DocumentType => DocumentType.Excel;

    public Task<Result<InvoiceExtractionResult>> ProcessAsync(BlobDocument document, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(document.Content);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.First();
            var rows = worksheet.RangeUsed()?.RowsUsed().ToList() ?? [];

            if (rows.Count < 2)
            {
                return Task.FromResult(Result<InvoiceExtractionResult>.Failure(new Error("excel.empty", "Excel file did not contain invoice rows.")));
            }

            var headers = rows[0].Cells().Select((cell, index) => new { index, name = cell.GetString() }).ToDictionary(x => x.index + 1, x => x.name, EqualityComparer<int>.Default);
            var invoice = new InvoiceHeader { SourceFileName = document.BlobName, SourceFileType = DocumentType.ToString(), ProcessingStatus = ProcessingStatus.Extracted, InvoiceConfidenceScore = 0.92m };

            foreach (var row in rows.Skip(1))
            {
                var line = new InvoiceLineItem();
                foreach (var cell in row.CellsUsed())
                {
                    var header = headers.GetValueOrDefault(cell.Address.ColumnNumber, string.Empty);
                    var value = cell.GetString();
                    switch (header)
                    {
                        case "InvoiceNumber":
                            invoice.InvoiceNumber = value;
                            break;
                        case "VendorName":
                            invoice.VendorName = value;
                            break;
                        case "VendorAddress":
                            invoice.VendorAddress = value;
                            break;
                        case "InvoiceDate":
                            invoice.InvoiceDate = DateOnly.TryParse(value, out var invoiceDate) ? invoiceDate : null;
                            break;
                        case "DueDate":
                            invoice.DueDate = DateOnly.TryParse(value, out var dueDate) ? dueDate : null;
                            break;
                        case "Currency":
                            invoice.Currency = value;
                            break;
                        case "Subtotal":
                            invoice.Subtotal = decimal.TryParse(value, out var subtotal) ? subtotal : null;
                            break;
                        case "TaxAmount":
                            invoice.TaxAmount = decimal.TryParse(value, out var tax) ? tax : null;
                            break;
                        case "TotalAmount":
                            invoice.TotalAmount = decimal.TryParse(value, out var total) ? total : null;
                            break;
                        case "PurchaseOrderNumber":
                            invoice.PurchaseOrderNumber = value;
                            break;
                        case "PaymentTerms":
                            invoice.PaymentTerms = value;
                            break;
                        case "CustomerName":
                            invoice.CustomerName = value;
                            break;
                        case "Description":
                            line.Description = value;
                            break;
                        case "Quantity":
                            line.Quantity = decimal.TryParse(value, out var quantity) ? quantity : null;
                            break;
                        case "UnitPrice":
                            line.UnitPrice = decimal.TryParse(value, out var unitPrice) ? unitPrice : null;
                            break;
                        case "Amount":
                            line.Amount = decimal.TryParse(value, out var amount) ? amount : null;
                            break;
                        case "Tax":
                            line.Tax = decimal.TryParse(value, out var lineTax) ? lineTax : null;
                            break;
                        case "SKU":
                            line.Sku = value;
                            break;
                        case "Unit":
                            line.Unit = value;
                            break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(line.Description))
                {
                    invoice.LineItems.Add(line);
                }
            }

            return Task.FromResult(Result<InvoiceExtractionResult>.Success(new InvoiceExtractionResult
            {
                IsSuccessful = true,
                DocumentType = DocumentType,
                Invoice = invoice,
                ProcessorName = nameof(ExcelProcessor)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<InvoiceExtractionResult>.Failure(new Error("excel.extract", ex.Message)));
        }
    }
}
