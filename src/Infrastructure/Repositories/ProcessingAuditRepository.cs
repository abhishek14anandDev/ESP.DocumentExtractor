using Dapper;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using ESP.DocumentExtractor.Infrastructure.Sql;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Repositories;

public sealed class ProcessingAuditRepository(
    ISqlConnectionFactory connectionFactory,
    IRetryPolicyExecutor retryPolicyExecutor,
    IOptions<SqlOptions> sqlOptions) : IProcessingAuditRepository
{
    public async Task SaveAsync(DocumentProcessingAudit audit, CancellationToken cancellationToken)
    {
        var result = await retryPolicyExecutor.ExecuteAsync(
            async token =>
            {
                await using var connection = (System.Data.Common.DbConnection)await connectionFactory.CreateOpenConnectionAsync(token);
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        SqlQueries.InsertDocumentProcessingAudit,
                        new
                        {
                            audit.CorrelationId,
                            audit.BlobName,
                            audit.ContainerName,
                            DocumentType = audit.DocumentType.ToString(),
                            ProcessingStatus = audit.ProcessingStatus.ToString(),
                            audit.Message,
                            audit.ErrorMessage,
                            audit.InvoiceHeaderId,
                            ProcessingDurationMilliseconds = audit.ProcessingDuration.TotalMilliseconds,
                            audit.ProcessedOn
                        },
                        commandTimeout: sqlOptions.Value.CommandTimeoutSeconds,
                        cancellationToken: token));

                return Result.Success();
            },
            "sql-save-audit",
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Domain.Exceptions.DatabaseException(result.Error.Message);
        }
    }
}
