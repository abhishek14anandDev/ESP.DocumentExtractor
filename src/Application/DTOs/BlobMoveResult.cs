namespace ESP.DocumentExtractor.Application.DTOs;

public sealed class BlobMoveResult
{
    public required string DestinationContainer { get; init; }
    public required string DestinationBlobName { get; init; }
}
