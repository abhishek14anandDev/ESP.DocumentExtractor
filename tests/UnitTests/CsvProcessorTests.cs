using System.Text;
using ESP.DocumentExtractor.Application.Strategies;
using ESP.DocumentExtractor.Domain.Models;
using FluentAssertions;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class CsvProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldExtractInvoiceAndLineItems()
    {
        const string csv = """
InvoiceNumber,VendorName,VendorAddress,InvoiceDate,DueDate,Currency,Subtotal,TaxAmount,TotalAmount,PurchaseOrderNumber,PaymentTerms,CustomerName,Description,Quantity,UnitPrice,Amount,Tax,SKU,Unit
INV-101,Contoso,1 Main St,2026-06-01,2026-06-30,USD,100,10,110,PO-1,Net 30,Fabrikam,Widget,2,50,100,10,SKU-1,EA
""";

        var sut = new CsvProcessor();

        var result = await sut.ProcessAsync(new BlobDocument
        {
            BlobName = "invoice.csv",
            ContainerName = "incoming",
            ContentType = "text/csv",
            Content = Encoding.UTF8.GetBytes(csv)
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoice.InvoiceNumber.Should().Be("INV-101");
        result.Value.Invoice.LineItems.Should().ContainSingle();
    }
}
