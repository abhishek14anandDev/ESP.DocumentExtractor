using ESP.DocumentExtractor.Domain.Entities;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IBlobProcessingHistoryRepository
{
    Task SaveAsync(BlobProcessingHistory history, CancellationToken cancellationToken);
}
