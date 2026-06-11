using ESP.DocumentExtractor.Domain.Entities;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IProcessingAuditRepository
{
    Task SaveAsync(DocumentProcessingAudit audit, CancellationToken cancellationToken);
}
