namespace ESP.DocumentExtractor.Infrastructure.Sql;

internal static class SqlQueries
{
    public const string InsertInvoiceHeader = """
        INSERT INTO dbo.InvoiceHeader
        (
            InvoiceNumber, VendorName, VendorAddress, InvoiceDate, DueDate, Currency,
            Subtotal, TaxAmount, TotalAmount, PurchaseOrderNumber, PaymentTerms, CustomerName,
            InvoiceConfidenceScore, SourceFileName, SourceFileType, ProcessingDate, ProcessingStatus,
            ErrorMessage, CreatedDate, UpdatedDate
        )
        VALUES
        (
            @InvoiceNumber, @VendorName, @VendorAddress, @InvoiceDate, @DueDate, @Currency,
            @Subtotal, @TaxAmount, @TotalAmount, @PurchaseOrderNumber, @PaymentTerms, @CustomerName,
            @InvoiceConfidenceScore, @SourceFileName, @SourceFileType, @ProcessingDate, @ProcessingStatus,
            @ErrorMessage, SYSUTCDATETIME(), SYSUTCDATETIME()
        );
        SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
        """;

    public const string InsertInvoiceLineItem = """
        INSERT INTO dbo.InvoiceLineItem
        (
            InvoiceHeaderId, Description, Quantity, UnitPrice, Amount, Tax, Sku, Unit, CreatedDate, UpdatedDate
        )
        VALUES
        (
            @InvoiceHeaderId, @Description, @Quantity, @UnitPrice, @Amount, @Tax, @Sku, @Unit,
            SYSUTCDATETIME(), SYSUTCDATETIME()
        );
        """;

    public const string InsertDocumentProcessingAudit = """
        INSERT INTO dbo.DocumentProcessingAudit
        (
            CorrelationId, BlobName, ContainerName, DocumentType, ProcessingStatus, Message,
            ErrorMessage, InvoiceHeaderId, ProcessingDurationMilliseconds, ProcessedOn, CreatedDate, UpdatedDate
        )
        VALUES
        (
            @CorrelationId, @BlobName, @ContainerName, @DocumentType, @ProcessingStatus, @Message,
            @ErrorMessage, @InvoiceHeaderId, @ProcessingDurationMilliseconds, @ProcessedOn, SYSUTCDATETIME(), SYSUTCDATETIME()
        );
        """;

    public const string InsertBlobProcessingHistory = """
        INSERT INTO dbo.BlobProcessingHistory
        (
            CorrelationId, BlobName, SourceContainer, DestinationContainer, DocumentType,
            ProcessingStatus, ErrorMessage, ProcessedOn, CreatedDate, UpdatedDate
        )
        VALUES
        (
            @CorrelationId, @BlobName, @SourceContainer, @DestinationContainer, @DocumentType,
            @ProcessingStatus, @ErrorMessage, @ProcessedOn, SYSUTCDATETIME(), SYSUTCDATETIME()
        );
        """;
}
