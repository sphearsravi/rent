-- LegalCases table — legal/court cases filed on a lease (tracked, tenant sees status).
-- Applied out-of-band (idempotent) because this database's schema has diverged from
-- the EF migration snapshot (EmailSettings / TenantDeviceTokens etc. exist in the DB
-- but not in the snapshot), so `dotnet ef database update` is unsafe here.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LegalCases')
BEGIN
    CREATE TABLE dbo.LegalCases
    (
        LegalCaseId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LegalCases PRIMARY KEY,
        CompanyId        INT               NOT NULL,
        LeaseId          INT               NOT NULL,
        TenantId         INT               NOT NULL,
        CaseNumber       NVARCHAR(100)     NULL,
        CaseType         NVARCHAR(50)      NOT NULL,
        [Status]         NVARCHAR(50)      NOT NULL,
        FiledDate        DATETIME2         NOT NULL,
        NextHearingDate  DATETIME2         NULL,
        AmountClaimed    DECIMAL(18,3)     NULL,
        Notes            NVARCHAR(MAX)     NULL,
        CreatedDate      DATETIME2         NOT NULL,
        CreatedBy        NVARCHAR(256)     NULL,
        ModifiedDate     DATETIME2         NULL,
        ModifiedBy       NVARCHAR(256)     NULL,
        CONSTRAINT FK_LegalCases_Leases_LeaseId
            FOREIGN KEY (LeaseId)  REFERENCES dbo.Leases(LeaseId)   ON DELETE NO ACTION,
        CONSTRAINT FK_LegalCases_Tenants_TenantId
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(TenantId) ON DELETE NO ACTION
    );

    CREATE INDEX IX_LegalCases_LeaseId  ON dbo.LegalCases(LeaseId);
    CREATE INDEX IX_LegalCases_TenantId ON dbo.LegalCases(TenantId);
    CREATE INDEX IX_LegalCases_CompanyId ON dbo.LegalCases(CompanyId);
END
