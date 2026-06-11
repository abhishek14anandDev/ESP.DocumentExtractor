using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IBlobService
{
    Task<Result<BlobDocument>> DownloadAsync(BlobPointer pointer, CancellationToken cancellationToken);
    Task<Result<BlobMoveResult>> MoveToProcessedAsync(BlobPointer pointer, CancellationToken cancellationToken);
    Task<Result<BlobMoveResult>> MoveToRejectedAsync(BlobPointer pointer, string reason, CancellationToken cancellationToken);
    Task<Result> UploadRawResponseAsync(RawResponseUploadRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(BlobPointer pointer, CancellationToken cancellationToken);
}
