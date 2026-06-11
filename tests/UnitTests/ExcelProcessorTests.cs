using ClosedXML.Excel;
using ESP.DocumentExtractor.Application.Strategies;
using ESP.DocumentExtractor.Domain.Models;
using FluentAssertions;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class ExcelProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldExtractInvoiceAndLineItems()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Invoice");
        var headers = new[]
        {
            "InvoiceNumber", "VendorName", "VendorAddress", "InvoiceDate", "DueDate", "Currency", "Subtotal",
            "TaxAmount", "TotalAmount", "PurchaseOrderNumber", "PaymentTerms", "CustomerName", "Description",
            "Quantity", "UnitPrice", "Amount", "Tax", "SKU", "Unit"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            worksheet.Cell(1, index + 1).Value = headers[index];
        }

        worksheet.Cell(2, 1).Value = "INV-201";
        worksheet.Cell(2, 2).Value = "Contoso";
        worksheet.Cell(2, 7).Value = 100;
        worksheet.Cell(2, 8).Value = 10;
        worksheet.Cell(2, 9).Value = 110;
        worksheet.Cell(2, 13).Value = "Widget";
        worksheet.Cell(2, 14).Value = 2;
        worksheet.Cell(2, 15).Value = 50;
        worksheet.Cell(2, 16).Value = 100;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var sut = new ExcelProcessor();
        var result = await sut.ProcessAsync(new BlobDocument
        {
            BlobName = "invoice.xlsx",
            ContainerName = "incoming",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Content = stream.ToArray()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoice.InvoiceNumber.Should().Be("INV-201");
        result.Value.Invoice.LineItems.Should().ContainSingle();
    }
}
