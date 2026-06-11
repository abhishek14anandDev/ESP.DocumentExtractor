using ESP.DocumentExtractor.Application.Validators;
using ESP.DocumentExtractor.Domain.Entities;
using FluentAssertions;

namespace ESP.DocumentExtractor.UnitTests;

public sealed class InvoiceExtractionValidatorTests
{
    private readonly InvoiceExtractionValidator _sut = new();

    [Fact]
    public void Validate_ShouldFail_WhenInvoiceNumberMissing()
    {
        var invoice = new InvoiceHeader
        {
            VendorName = "Vendor",
            TotalAmount = 10
        };

        var result = _sut.Validate(invoice);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldPass_WhenRequiredFieldsExist()
    {
        var invoice = new InvoiceHeader
        {
            InvoiceNumber = "INV-100",
            VendorName = "Vendor",
            TotalAmount = 10
        };

        var result = _sut.Validate(invoice);

        result.IsSuccess.Should().BeTrue();
    }
}
