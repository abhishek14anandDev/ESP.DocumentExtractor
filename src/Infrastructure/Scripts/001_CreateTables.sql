CREATE TABLE dbo.InvoiceHeader
(
    InvoiceHeaderId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceHeader PRIMARY KEY,
    InvoiceNumber NVARCHAR(100) NOT NULL,
    VendorName NVARCHAR(200) NOT NULL,
    VendorAddress NVARCHAR(500) NULL,
    InvoiceDate DATE NULL,
    DueDate DATE NULL,
    Currency NVARCHAR(10) NULL,
    Subtotal DECIMAL(18, 2) NULL,
    TaxAmount DECIMAL(18, 2) NULL,
    TotalAmount DECIMAL(18, 2) NULL,
    PurchaseOrderNumber NVARCHAR(100) NULL,
    PaymentTerms NVARCHAR(100) NULL,
    CustomerName NVARCHAR(200) NULL,
    InvoiceConfidenceScore DECIMAL(9, 4) NOT NULL,
    SourceFileName NVARCHAR(400) NOT NULL,
    SourceFileType NVARCHAR(50) NOT NULL,
    ProcessingDate DATETIMEOFFSET NOT NULL,
    ProcessingStatus NVARCHAR(50) NOT NULL,
    ErrorMessage NVARCHAR(2000) NULL,
    CreatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_InvoiceHeader_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_InvoiceHeader_UpdatedDate DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.InvoiceLineItem
(
    InvoiceLineItemId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceLineItem PRIMARY KEY,
    InvoiceHeaderId BIGINT NOT NULL,
    Description NVARCHAR(500) NOT NULL,
    Quantity DECIMAL(18, 4) NULL,
    UnitPrice DECIMAL(18, 4) NULL,
    Amount DECIMAL(18, 2) NULL,
    Tax DECIMAL(18, 2) NULL,
    Sku NVARCHAR(100) NULL,
    Unit NVARCHAR(50) NULL,
    CreatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_InvoiceLineItem_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_InvoiceLineItem_UpdatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_InvoiceLineItem_InvoiceHeader FOREIGN KEY (InvoiceHeaderId) REFERENCES dbo.InvoiceHeader(InvoiceHeaderId)
);

CREATE TABLE dbo.DocumentProcessingAudit
(
    DocumentProcessingAuditId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentProcessingAudit PRIMARY KEY,
    CorrelationId NVARCHAR(64) NOT NULL,
    BlobName NVARCHAR(400) NOT NULL,
    ContainerName NVARCHAR(100) NOT NULL,
    DocumentType NVARCHAR(50) NOT NULL,
    ProcessingStatus NVARCHAR(50) NOT NULL,
    Message NVARCHAR(1000) NOT NULL,
    ErrorMessage NVARCHAR(2000) NULL,
    InvoiceHeaderId BIGINT NULL,
    ProcessingDurationMilliseconds BIGINT NOT NULL,
    ProcessedOn DATETIMEOFFSET NOT NULL,
    CreatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_DocumentProcessingAudit_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_DocumentProcessingAudit_UpdatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_DocumentProcessingAudit_InvoiceHeader FOREIGN KEY (InvoiceHeaderId) REFERENCES dbo.InvoiceHeader(InvoiceHeaderId)
);

CREATE TABLE dbo.BlobProcessingHistory
(
    BlobProcessingHistoryId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BlobProcessingHistory PRIMARY KEY,
    CorrelationId NVARCHAR(64) NOT NULL,
    BlobName NVARCHAR(400) NOT NULL,
    SourceContainer NVARCHAR(100) NOT NULL,
    DestinationContainer NVARCHAR(100) NULL,
    DocumentType NVARCHAR(50) NOT NULL,
    ProcessingStatus NVARCHAR(50) NOT NULL,
    ErrorMessage NVARCHAR(2000) NULL,
    ProcessedOn DATETIMEOFFSET NOT NULL,
    CreatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_BlobProcessingHistory_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_BlobProcessingHistory_UpdatedDate DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_InvoiceHeader_InvoiceNumber ON dbo.InvoiceHeader (InvoiceNumber);
CREATE INDEX IX_InvoiceHeader_ProcessingDate ON dbo.InvoiceHeader (ProcessingDate DESC);
CREATE INDEX IX_InvoiceLineItem_InvoiceHeaderId ON dbo.InvoiceLineItem (InvoiceHeaderId);
CREATE INDEX IX_DocumentProcessingAudit_CorrelationId ON dbo.DocumentProcessingAudit (CorrelationId);
CREATE INDEX IX_DocumentProcessingAudit_ProcessedOn ON dbo.DocumentProcessingAudit (ProcessedOn DESC);
CREATE INDEX IX_BlobProcessingHistory_CorrelationId ON dbo.BlobProcessingHistory (CorrelationId);
CREATE INDEX IX_BlobProcessingHistory_ProcessedOn ON dbo.BlobProcessingHistory (ProcessedOn DESC);
