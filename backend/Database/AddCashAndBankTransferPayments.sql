-- Migration: Add Cash and Bank Transfer Payment Support
-- Date: 2025-11-13
-- Description: Adds tables and columns for Cash and Bank Transfer payment methods

-- Step 1: Add new columns to Payments table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'InvoiceId')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [InvoiceId] INT NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'PropertyId')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [PropertyId] INT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'PaymentType')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [PaymentType] INT NOT NULL DEFAULT 1; -- 1=KNET, 2=Cash, 3=BankTransfer
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'Status')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [Status] INT NOT NULL DEFAULT 2; -- 1=Pending, 2=Completed, 5=PendingVerification, 6=Approved, 7=Rejected
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'CollectedBy')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [CollectedBy] NVARCHAR(200) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'ApprovedBy')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [ApprovedBy] NVARCHAR(200) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'ApprovedDate')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [ApprovedDate] DATETIME2 NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'ReceiptNumber')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [ReceiptNumber] NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'ModifiedDate')
BEGIN
    ALTER TABLE [dbo].[Payments] ADD [ModifiedDate] DATETIME2 NULL;
END
GO

-- Step 2: Create CashPayments table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CashPayments]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[CashPayments] (
        [CashPaymentId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [PaymentId] INT NOT NULL,
        [TenantId] INT NOT NULL,
        [PropertyId] INT NOT NULL,
        [InvoiceId] INT NULL,
        [Amount] DECIMAL(18,2) NOT NULL,
        [PaymentDate] DATETIME2 NOT NULL,
        [CollectedBy] NVARCHAR(200) NOT NULL,
        [ReceiptNumber] NVARCHAR(100) NULL,
        [Notes] NVARCHAR(MAX) NULL,
        [CreatedDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy] NVARCHAR(200) NOT NULL,
        CONSTRAINT FK_CashPayments_Payments FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[Payments]([PaymentId]) ON DELETE CASCADE,
        CONSTRAINT FK_CashPayments_Tenants FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
        CONSTRAINT FK_CashPayments_Properties FOREIGN KEY ([PropertyId]) REFERENCES [dbo].[Properties]([PropertyId]),
        CONSTRAINT FK_CashPayments_Invoices FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[Invoices]([InvoiceId])
    );

    CREATE INDEX IX_CashPayments_PaymentDate ON [dbo].[CashPayments]([PaymentDate]);
    CREATE INDEX IX_CashPayments_ReceiptNumber ON [dbo].[CashPayments]([ReceiptNumber]);
    CREATE INDEX IX_CashPayments_TenantId ON [dbo].[CashPayments]([TenantId]);
    CREATE INDEX IX_CashPayments_PropertyId ON [dbo].[CashPayments]([PropertyId]);
END
GO

-- Step 3: Create BankTransfers table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[BankTransfers]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[BankTransfers] (
        [BankTransferId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [PaymentId] INT NOT NULL,
        [TenantId] INT NOT NULL,
        [PropertyId] INT NOT NULL,
        [InvoiceId] INT NULL,
        [Amount] DECIMAL(18,2) NOT NULL,
        [BankName] NVARCHAR(200) NOT NULL,
        [TransferDate] DATETIME2 NOT NULL,
        [ReferenceNumber] NVARCHAR(200) NULL,
        [ProofFilePath] NVARCHAR(500) NOT NULL,
        [ProofFileName] NVARCHAR(300) NOT NULL,
        [ProofFileType] NVARCHAR(50) NULL,
        [ProofFileSize] BIGINT NULL,
        [Status] INT NOT NULL DEFAULT 1, -- 1=PendingVerification, 2=Approved, 3=Rejected
        [Notes] NVARCHAR(MAX) NULL,
        [ApprovedBy] NVARCHAR(200) NULL,
        [ApprovedDate] DATETIME2 NULL,
        [RejectionReason] NVARCHAR(500) NULL,
        [SubmittedDate] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [SubmittedBy] NVARCHAR(200) NOT NULL,
        CONSTRAINT FK_BankTransfers_Payments FOREIGN KEY ([PaymentId]) REFERENCES [dbo].[Payments]([PaymentId]) ON DELETE CASCADE,
        CONSTRAINT FK_BankTransfers_Tenants FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
        CONSTRAINT FK_BankTransfers_Properties FOREIGN KEY ([PropertyId]) REFERENCES [dbo].[Properties]([PropertyId]),
        CONSTRAINT FK_BankTransfers_Invoices FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[Invoices]([InvoiceId])
    );

    CREATE INDEX IX_BankTransfers_Status ON [dbo].[BankTransfers]([Status]);
    CREATE INDEX IX_BankTransfers_SubmittedDate ON [dbo].[BankTransfers]([SubmittedDate]);
    CREATE INDEX IX_BankTransfers_ReferenceNumber ON [dbo].[BankTransfers]([ReferenceNumber]);
    CREATE INDEX IX_BankTransfers_TenantId ON [dbo].[BankTransfers]([TenantId]);
    CREATE INDEX IX_BankTransfers_PropertyId ON [dbo].[BankTransfers]([PropertyId]);
END
GO

-- Step 4: Add payment configuration columns to Properties table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = 'AcceptKNET')
BEGIN
    ALTER TABLE [dbo].[Properties] ADD [AcceptKNET] BIT NOT NULL DEFAULT 1;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = 'AcceptCash')
BEGIN
    ALTER TABLE [dbo].[Properties] ADD [AcceptCash] BIT NOT NULL DEFAULT 1;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = 'AcceptBankTransfer')
BEGIN
    ALTER TABLE [dbo].[Properties] ADD [AcceptBankTransfer] BIT NOT NULL DEFAULT 1;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = 'KNETMerchantId')
BEGIN
    ALTER TABLE [dbo].[Properties] ADD [KNETMerchantId] NVARCHAR(200) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = 'KNETMerchantKey')
BEGIN
    ALTER TABLE [dbo].[Properties] ADD [KNETMerchantKey] NVARCHAR(500) NULL;
END
GO

-- Step 5: Add indexes on new Payment columns
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Payments_Status' AND object_id = OBJECT_ID(N'[dbo].[Payments]'))
BEGIN
    CREATE INDEX IX_Payments_Status ON [dbo].[Payments]([Status]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Payments_PaymentType' AND object_id = OBJECT_ID(N'[dbo].[Payments]'))
BEGIN
    CREATE INDEX IX_Payments_PaymentType ON [dbo].[Payments]([PaymentType]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Payments_PropertyId' AND object_id = OBJECT_ID(N'[dbo].[Payments]'))
BEGIN
    CREATE INDEX IX_Payments_PropertyId ON [dbo].[Payments]([PropertyId]);
END
GO

-- Step 6: Add foreign key for Payment.PropertyId
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Payments_Properties')
BEGIN
    ALTER TABLE [dbo].[Payments]
    ADD CONSTRAINT FK_Payments_Properties FOREIGN KEY ([PropertyId])
    REFERENCES [dbo].[Properties]([PropertyId]);
END
GO

-- Step 7: Add foreign key for Payment.InvoiceId
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Payments_Invoices')
BEGIN
    ALTER TABLE [dbo].[Payments]
    ADD CONSTRAINT FK_Payments_Invoices FOREIGN KEY ([InvoiceId])
    REFERENCES [dbo].[Invoices]([InvoiceId]);
END
GO

PRINT 'Migration completed: Cash and Bank Transfer payment support added successfully';
GO
