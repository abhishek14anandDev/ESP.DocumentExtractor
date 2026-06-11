using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ESP.DocumentExtractor.Application.DTOs;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Exceptions;
using ESP.DocumentExtractor.Domain.Models;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Blob;

public sealed class BlobService(
    BlobServiceClient blobServiceClient,
    IOptions<BlobOptions> blobOptions,
    IRetryPolicyExecutor retryPolicyExecutor,
    ILogger<BlobService> logger) : IBlobService
{
    public Task<Result<BlobDocument>> DownloadAsync(BlobPointer pointer, CancellationToken cancellationToken) =>
        retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                try
                {
                    var blobClient = GetContainerClient(pointer.ContainerName).GetBlobClient(pointer.BlobName);
                    var response = await blobClient.DownloadContentAsync(token);
                    return Result<BlobDocument>.Success(new BlobDocument
                    {
                        BlobName = pointer.BlobName,
                        ContainerName = pointer.ContainerName,
                        ContentType = response.Value.Details.ContentType ?? "application/octet-stream",
                        Content = response.Value.Content.ToArray(),
                        ETag = response.Value.Details.ETag.ToString(),
                        CorrelationId = pointer.CorrelationId
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Blob download failed for {ContainerName}/{BlobName}.", pointer.ContainerName, pointer.BlobName);
                    return Result<BlobDocument>.Failure(new Error("blob.download", ex.Message));
                }
            },
            "blob-download",
            cancellationToken);

    public Task<Result<BlobMoveResult>> MoveToProcessedAsync(BlobPointer pointer, CancellationToken cancellationToken) =>
        MoveAsync(pointer, blobOptions.Value.ProcessedContainer, cancellationToken);

    public Task<Result<BlobMoveResult>> MoveToRejectedAsync(BlobPointer pointer, string reason, CancellationToken cancellationToken) =>
        MoveAsync(pointer, blobOptions.Value.RejectedContainer, cancellationToken, reason);

    public Task<Result> UploadRawResponseAsync(RawResponseUploadRequest request, CancellationToken cancellationToken) =>
        retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                try
                {
                    var blobName = $"{request.CorrelationId}/{Path.GetFileNameWithoutExtension(request.BlobName)}.json";
                    var blobClient = GetContainerClient(blobOptions.Value.RawResponsesContainer).GetBlobClient(blobName);
                    await blobClient.UploadAsync(BinaryData.FromString(request.Content), overwrite: true, cancellationToken: token);
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Raw response upload failed for {BlobName}.", request.BlobName);
                    return Result.Failure(new Error("blob.rawResponse", ex.Message));
                }
            },
            "blob-upload-raw-response",
            cancellationToken);

    public Task<Result> DeleteAsync(BlobPointer pointer, CancellationToken cancellationToken) =>
        retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                try
                {
                    var blobClient = GetContainerClient(pointer.ContainerName).GetBlobClient(pointer.BlobName);
                    await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: token);
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Result.Failure(new Error("blob.delete", ex.Message));
                }
            },
            "blob-delete",
            cancellationToken);

    private Task<Result<BlobMoveResult>> MoveAsync(BlobPointer pointer, string targetContainer, CancellationToken cancellationToken, string? reason = null) =>
        retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                try
                {
                    var source = GetContainerClient(pointer.ContainerName).GetBlobClient(pointer.BlobName);
                    var destination = GetContainerClient(targetContainer).GetBlobClient(pointer.BlobName);

                    await destination.StartCopyFromUriAsync(source.Uri, cancellationToken: token);
                    await source.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: token);

                    logger.LogInformation(
                        "Blob moved from {SourceContainer} to {DestinationContainer}. Reason: {Reason}",
                        pointer.ContainerName,
                        targetContainer,
                        reason);

                    return Result<BlobMoveResult>.Success(new BlobMoveResult
                    {
                        DestinationContainer = targetContainer,
                        DestinationBlobName = pointer.BlobName
                    });
                }
                catch (RequestFailedException ex)
                {
                    logger.LogError(ex, "Blob move failed for {BlobName}.", pointer.BlobName);
                    return Result<BlobMoveResult>.Failure(new Error("blob.move", ex.Message));
                }
            },
            "blob-move",
            cancellationToken);

    private BlobContainerClient GetContainerClient(string containerName)
    {
        var client = blobServiceClient.GetBlobContainerClient(containerName);
        client.CreateIfNotExists(PublicAccessType.None);
        return client;
    }
}
