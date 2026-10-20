-- ROLLBACK for db_improve.sql (2026-10-12): restores the previous FK delete rules and drops the added indexes.
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRANSACTION;
ALTER TABLE BankTransfers DROP CONSTRAINT FK_BankTransfers_Payments;
ALTER TABLE BankTransfers ADD CONSTRAINT FK_BankTransfers_Payments FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE CASCADE;
ALTER TABLE CashPayments DROP CONSTRAINT FK_CashPayments_Payments;
ALTER TABLE CashPayments ADD CONSTRAINT FK_CashPayments_Payments FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE CASCADE;
ALTER TABLE PaymentAudits DROP CONSTRAINT FK_PaymentAudits_Payments_PaymentId;
ALTER TABLE PaymentAudits ADD CONSTRAINT FK_PaymentAudits_Payments_PaymentId FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE CASCADE;
ALTER TABLE Refunds DROP CONSTRAINT FK_Refunds_Payments_PaymentId;
ALTER TABLE Refunds ADD CONSTRAINT FK_Refunds_Payments_PaymentId FOREIGN KEY (PaymentId) REFERENCES Payments(PaymentId) ON DELETE CASCADE;
ALTER TABLE Units DROP CONSTRAINT fk_units_property;
ALTER TABLE Units ADD CONSTRAINT fk_units_property FOREIGN KEY (PropertyId) REFERENCES Properties(PropertyId) ON DELETE CASCADE;
DROP INDEX IF EXISTS UQ_Payments_TransactionReference ON Payments;
DROP INDEX IF EXISTS IX_Payments_InvoiceId ON Payments;
DROP INDEX IF EXISTS IX_Payments_Tenant_Status_Date ON Payments;
DROP INDEX IF EXISTS IX_Invoices_Tenant_Status_Period ON Invoices;
DROP INDEX IF EXISTS IX_Notifications_Status_NextAttemptAt ON Notifications;
DROP INDEX IF EXISTS IX_Refunds_Status ON Refunds;
DROP INDEX IF EXISTS IX_Units_Property_Status ON Units;
DROP INDEX IF EXISTS UQ_Tenants_GovernmentId ON Tenants;
-- Notifications.Status was widened back only if you truly need (max); nvarchar(32) is otherwise strictly better.
ALTER TABLE Notifications ALTER COLUMN Status nvarchar(max) NOT NULL;
COMMIT TRANSACTION;
PRINT 'ROLLED BACK';
