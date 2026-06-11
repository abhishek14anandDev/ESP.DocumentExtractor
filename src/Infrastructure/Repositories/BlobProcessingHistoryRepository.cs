using Dapper;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using ESP.DocumentExtractor.Infrastructure.Sql;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Repositories;

public sealed class BlobProcessingHistoryRepository(
    ISqlConnectionFactory connectionFactory,
    IRetryPolicyExecutor retryPolicyExecutor,
    IOptions<SqlOptions> sqlOptions) : IBlobProcessingHistoryRepository
{
    public async Task SaveAsync(BlobProcessingHistory history, CancellationToken cancellationToken)
    {
        var result = await retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                await using var connection = (System.Data.Common.DbConnection)await connectionFactory.CreateOpenConnectionAsync(token);
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        SqlQueries.InsertBlobProcessingHistory,
                        new
                        {
                            history.CorrelationId,
                            history.BlobName,
                            history.SourceContainer,
                            history.DestinationContainer,
                            DocumentType = history.DocumentType.ToString(),
                            ProcessingStatus = history.ProcessingStatus.ToString(),
                            history.ErrorMessage,
                            history.ProcessedOn
                        },
                        commandTimeout: sqlOptions.Value.CommandTimeoutSeconds,
                        cancellationToken: token));

                return Result.Success();
            },
            "sql-save-blob-history",
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Domain.Exceptions.DatabaseException(result.Error.Message);
        }
    }
}
