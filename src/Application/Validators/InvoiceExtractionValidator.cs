using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Validators;

public sealed class InvoiceExtractionValidator : IInvoiceExtractionValidator
{
    public Result Validate(InvoiceHeader invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            return Result.Failure(new Error("validation.invoiceNumber", "Invoice number is required."));
        }

        if (string.IsNullOrWhiteSpace(invoice.VendorName))
        {
            return Result.Failure(new Error("validation.vendorName", "Vendor name is required."));
        }

        if (!invoice.TotalAmount.HasValue || invoice.TotalAmount.Value <= 0)
        {
            return Result.Failure(new Error("validation.totalAmount", "Total amount must be greater than zero."));
        }

        if (invoice.LineItems.Any() && invoice.LineItems.Any(x => string.IsNullOrWhiteSpace(x.Description)))
        {
            return Result.Failure(new Error("validation.lineItems", "Line items must have descriptions."));
        }

        return Result.Success();
    }
}
