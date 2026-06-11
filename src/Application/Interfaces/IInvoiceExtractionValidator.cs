using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IInvoiceExtractionValidator
{
    Result Validate(InvoiceHeader invoice);
}
