-- Alrtam production DB improvements — 2026-10-12
-- Structural only: FK delete rules + indexes + statistics. No row data modified.
-- Each section is its own batch so one failure cannot undo the others.
-- Rollback: db_improve_rollback.sql
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ===== 1) FK delete rules: financial/audit records must never cascade away =====
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_BankTransfers_Payments' AND delete_referential_action<>0)
BEGIN
  ALTER TABLE BankTransfers DROP CONSTRAINT FK_BankTransfers_Payments;
  ALTER TABLE BankTransfers ADD CONSTRAINT FK_BankTransfers_Payments FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE NO ACTION;
  PRINT 'FK_BankTransfers_Payments -> NO ACTION';
END
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_CashPayments_Payments' AND delete_referential_action<>0)
BEGIN
  ALTER TABLE CashPayments DROP CONSTRAINT FK_CashPayments_Payments;
  ALTER TABLE CashPayments ADD CONSTRAINT FK_CashPayments_Payments FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE NO ACTION;
  PRINT 'FK_CashPayments_Payments -> NO ACTION';
END
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_PaymentAudits_Payments_PaymentId' AND delete_referential_action<>0)
BEGIN
  ALTER TABLE PaymentAudits DROP CONSTRAINT FK_PaymentAudits_Payments_PaymentId;
  ALTER TABLE PaymentAudits ADD CONSTRAINT FK_PaymentAudits_Payments_PaymentId FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE NO ACTION;
  PRINT 'FK_PaymentAudits_Payments_PaymentId -> NO ACTION';
END
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_Refunds_Payments_PaymentId' AND delete_referential_action<>0)
BEGIN
  ALTER TABLE Refunds DROP CONSTRAINT FK_Refunds_Payments_PaymentId;
  ALTER TABLE Refunds ADD CONSTRAINT FK_Refunds_Payments_PaymentId FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE NO ACTION;
  PRINT 'FK_Refunds_Payments_PaymentId -> NO ACTION';
END
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='fk_units_property' AND delete_referential_action<>0)
BEGIN
  ALTER TABLE Units DROP CONSTRAINT fk_units_property;
  ALTER TABLE Units ADD CONSTRAINT fk_units_property FOREIGN KEY (PropertyId) REFERENCES Properties(PropertyId) ON DELETE NO ACTION;
  PRINT 'fk_units_property -> NO ACTION';
END
COMMIT TRANSACTION;
PRINT 'FK section committed';
GO
-- ===== 2) Indexes — each guarded, each its own batch =====
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UQ_Payments_TransactionReference')
  CREATE UNIQUE NONCLUSTERED INDEX UQ_Payments_TransactionReference ON Payments(TransactionReference) WHERE TransactionReference IS NOT NULL;
PRINT 'UQ_Payments_TransactionReference ok';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Payments_InvoiceId')
  CREATE NONCLUSTERED INDEX IX_Payments_InvoiceId ON Payments(InvoiceId);
PRINT 'IX_Payments_InvoiceId ok';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Payments_Tenant_Status_Date')
  CREATE NONCLUSTERED INDEX IX_Payments_Tenant_Status_Date ON Payments(TenantId, Status, PaymentDate);
PRINT 'IX_Payments_Tenant_Status_Date ok';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Invoices_Tenant_Status_Period')
  CREATE NONCLUSTERED INDEX IX_Invoices_Tenant_Status_Period ON Invoices(TenantId, Status, InvoiceDate);
PRINT 'IX_Invoices_Tenant_Status_Period ok';
GO
-- Notifications.Status is nvarchar(max) (un-indexable). Values are only Pending/Sent/Failed:
-- narrow it to nvarchar(32) — guarded: only when it is still (max), no NULLs, and every value fits.
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='Status' AND CHARACTER_MAXIMUM_LENGTH=-1)
   AND NOT EXISTS (SELECT 1 FROM Notifications WHERE Status IS NULL OR LEN(Status) > 32)
BEGIN
  ALTER TABLE Notifications ALTER COLUMN Status nvarchar(32) NOT NULL;
  PRINT 'Notifications.Status -> nvarchar(32) NOT NULL';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Notifications_Status_NextAttemptAt')
   AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='Status' AND CHARACTER_MAXIMUM_LENGTH BETWEEN 1 AND 450)
  CREATE NONCLUSTERED INDEX IX_Notifications_Status_NextAttemptAt ON Notifications(Status, NextAttemptAt);
PRINT 'IX_Notifications_Status_NextAttemptAt ok (or skipped)';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Refunds_Status')
   AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Refunds' AND COLUMN_NAME='Status' AND (CHARACTER_MAXIMUM_LENGTH IS NULL OR CHARACTER_MAXIMUM_LENGTH BETWEEN 1 AND 450))
  CREATE NONCLUSTERED INDEX IX_Refunds_Status ON Refunds(Status);
PRINT 'IX_Refunds_Status ok (or skipped)';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Units_Property_Status')
  CREATE NONCLUSTERED INDEX IX_Units_Property_Status ON Units(PropertyId, Status);
PRINT 'IX_Units_Property_Status ok';
GO
-- ===== 3) Fresh statistics =====
UPDATE STATISTICS Invoices; UPDATE STATISTICS Payments; UPDATE STATISTICS Leases; UPDATE STATISTICS Units;
UPDATE STATISTICS Tenants; UPDATE STATISTICS Properties; UPDATE STATISTICS Notifications; UPDATE STATISTICS MaintenanceRequests;
PRINT 'statistics updated';
GO
-- ===== 4) Verification =====
SELECT fk.name COLLATE DATABASE_DEFAULT + ' ON DELETE ' + fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT FROM sys.foreign_keys fk WHERE fk.name IN ('FK_BankTransfers_Payments','FK_CashPayments_Payments','FK_PaymentAudits_Payments_PaymentId','FK_Refunds_Payments_PaymentId','fk_units_property') ORDER BY 1;
SELECT 'index ' + name COLLATE DATABASE_DEFAULT FROM sys.indexes WHERE name IN ('UQ_Payments_TransactionReference','IX_Payments_InvoiceId','IX_Payments_Tenant_Status_Date','IX_Invoices_Tenant_Status_Period','IX_Notifications_Status_NextAttemptAt','IX_Refunds_Status','IX_Units_Property_Status') ORDER BY 1;
SELECT 'Notifications.Status type: ' + DATA_TYPE + '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS varchar) + ')' FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Notifications' AND COLUMN_NAME='Status';
