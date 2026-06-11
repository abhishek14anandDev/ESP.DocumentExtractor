using Dapper;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.Entities;
using ESP.DocumentExtractor.Domain.Exceptions;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using ESP.DocumentExtractor.Infrastructure.Sql;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Repositories;

public sealed class InvoiceRepository(
    ISqlConnectionFactory connectionFactory,
    IRetryPolicyExecutor retryPolicyExecutor,
    IOptions<SqlOptions> sqlOptions) : IInvoiceRepository
{
    public Task<long> SaveAsync(InvoiceHeader invoiceHeader, CancellationToken cancellationToken) =>
        ExecutePersistAsync(invoiceHeader, cancellationToken);

    private async Task<long> ExecutePersistAsync(InvoiceHeader invoiceHeader, CancellationToken cancellationToken)
    {
        var result = await retryPolicyExecutor.ExecuteAsync<long>(
            async token =>
            {
                try
                {
                    await using var connection = (System.Data.Common.DbConnection)await connectionFactory.CreateOpenConnectionAsync(token);
                    await using var transaction = await connection.BeginTransactionAsync(token);

                    var invoiceHeaderId = await connection.ExecuteScalarAsync<long>(
                        new CommandDefinition(
                            SqlQueries.InsertInvoiceHeader,
                            new
                            {
                                invoiceHeader.InvoiceNumber,
                                invoiceHeader.VendorName,
                                invoiceHeader.VendorAddress,
                                invoiceHeader.InvoiceDate,
                                invoiceHeader.DueDate,
                                invoiceHeader.Currency,
                                invoiceHeader.Subtotal,
                                invoiceHeader.TaxAmount,
                                invoiceHeader.TotalAmount,
                                invoiceHeader.PurchaseOrderNumber,
                                invoiceHeader.PaymentTerms,
                                invoiceHeader.CustomerName,
                                invoiceHeader.InvoiceConfidenceScore,
                                invoiceHeader.SourceFileName,
                                invoiceHeader.SourceFileType,
                                invoiceHeader.ProcessingDate,
                                ProcessingStatus = invoiceHeader.ProcessingStatus.ToString(),
                                invoiceHeader.ErrorMessage
                            },
                            transaction,
                            sqlOptions.Value.CommandTimeoutSeconds,
                            cancellationToken: token));

                    foreach (var lineItem in invoiceHeader.LineItems)
                    {
                        lineItem.InvoiceHeaderId = invoiceHeaderId;
                        await connection.ExecuteAsync(
                            new CommandDefinition(
                                SqlQueries.InsertInvoiceLineItem,
                                lineItem,
                                transaction,
                                sqlOptions.Value.CommandTimeoutSeconds,
                                cancellationToken: token));
                    }

                    await transaction.CommitAsync(token);
                    return Result<long>.Success(invoiceHeaderId);
                }
                catch (Exception ex)
                {
                    return Result<long>.Failure(new Error("sql.invoice.save", ex.Message));
                }
            },
            "sql-save-invoice",
            cancellationToken);

        if (result.IsFailure)
        {
            throw new DatabaseException(result.Error.Message);
        }

        return result.Value;
    }
}
