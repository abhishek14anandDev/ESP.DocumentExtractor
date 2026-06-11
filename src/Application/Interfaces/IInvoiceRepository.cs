using ESP.DocumentExtractor.Domain.Entities;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IInvoiceRepository
{
    Task<long> SaveAsync(InvoiceHeader invoiceHeader, CancellationToken cancellationToken);
}
