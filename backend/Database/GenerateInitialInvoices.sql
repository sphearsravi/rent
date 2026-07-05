-- Generate initial invoices for all active leases
USE RentCollectionDB;
GO

PRINT 'Generating invoices for active leases...';
PRINT '';

-- Generate invoices for the current month
DECLARE @CurrentMonth INT = MONTH(GETDATE());
DECLARE @CurrentYear INT = YEAR(GETDATE());
DECLARE @InvoicesGenerated INT = 0;

-- Get all active leases
DECLARE @LeaseId INT, @TenantId INT, @MonthlyRent DECIMAL(18,3), @PaymentDueDay INT, @UnitNumber NVARCHAR(50);

DECLARE lease_cursor CURSOR FOR
SELECT l.LeaseId, l.TenantId, l.MonthlyRent, l.PaymentDueDay, u.UnitNumber
FROM Leases l
INNER JOIN Units u ON l.UnitId = u.UnitId
WHERE l.Status = 'Active';

OPEN lease_cursor;
FETCH NEXT FROM lease_cursor INTO @LeaseId, @TenantId, @MonthlyRent, @PaymentDueDay, @UnitNumber;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Check if invoice already exists for this lease in current month
    IF NOT EXISTS (
        SELECT 1 FROM Invoices
        WHERE LeaseId = @LeaseId
        AND MONTH(InvoiceDate) = @CurrentMonth
        AND YEAR(InvoiceDate) = @CurrentYear
    )
    BEGIN
        -- Calculate due date
        DECLARE @DueDay INT = @PaymentDueDay;
        DECLARE @DaysInMonth INT = DAY(EOMONTH(GETDATE()));

        IF @DueDay > @DaysInMonth
            SET @DueDay = @DaysInMonth;

        DECLARE @DueDate DATE = DATEFROMPARTS(@CurrentYear, @CurrentMonth, @DueDay);

        -- Create invoice
        INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, Status, InvoiceType, Notes, CreatedDate)
        VALUES (
            @LeaseId,
            @TenantId,
            GETDATE(),
            @DueDate,
            @MonthlyRent,
            0,
            CASE
                WHEN @DueDate < CAST(GETDATE() AS DATE) THEN 'Overdue'
                ELSE 'Pending'
            END,
            'Rent',
            CONCAT('Monthly rent for ', FORMAT(GETDATE(), 'MMMM yyyy'), ' - Unit ', @UnitNumber),
            GETDATE()
        );

        SET @InvoicesGenerated = @InvoicesGenerated + 1;
    END

    FETCH NEXT FROM lease_cursor INTO @LeaseId, @TenantId, @MonthlyRent, @PaymentDueDay, @UnitNumber;
END

CLOSE lease_cursor;
DEALLOCATE lease_cursor;

-- Also generate invoices for previous month (for demonstration)
SET @CurrentMonth = @CurrentMonth - 1;
IF @CurrentMonth = 0
BEGIN
    SET @CurrentMonth = 12;
    SET @CurrentYear = @CurrentYear - 1;
END

DECLARE prev_lease_cursor CURSOR FOR
SELECT l.LeaseId, l.TenantId, l.MonthlyRent, l.PaymentDueDay, u.UnitNumber
FROM Leases l
INNER JOIN Units u ON l.UnitId = u.UnitId
WHERE l.Status = 'Active';

OPEN prev_lease_cursor;
FETCH NEXT FROM prev_lease_cursor INTO @LeaseId, @TenantId, @MonthlyRent, @PaymentDueDay, @UnitNumber;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Check if invoice already exists for this lease in previous month
    IF NOT EXISTS (
        SELECT 1 FROM Invoices
        WHERE LeaseId = @LeaseId
        AND MONTH(InvoiceDate) = @CurrentMonth
        AND YEAR(InvoiceDate) = @CurrentYear
    )
    BEGIN
        DECLARE @PrevDueDay INT = @PaymentDueDay;
        DECLARE @PrevDaysInMonth INT = DAY(EOMONTH(DATEFROMPARTS(@CurrentYear, @CurrentMonth, 1)));

        IF @PrevDueDay > @PrevDaysInMonth
            SET @PrevDueDay = @PrevDaysInMonth;

        DECLARE @PrevInvoiceDate DATE = DATEFROMPARTS(@CurrentYear, @CurrentMonth, 1);
        DECLARE @PrevDueDate DATE = DATEFROMPARTS(@CurrentYear, @CurrentMonth, @PrevDueDay);

        -- Create invoice for previous month (mark some as paid)
        DECLARE @RandomPaid INT = ABS(CHECKSUM(NEWID())) % 3; -- Random 0, 1, or 2

        INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, Status, InvoiceType, Notes, CreatedDate)
        VALUES (
            @LeaseId,
            @TenantId,
            @PrevInvoiceDate,
            @PrevDueDate,
            @MonthlyRent,
            CASE WHEN @RandomPaid = 0 THEN @MonthlyRent ELSE 0 END,
            CASE WHEN @RandomPaid = 0 THEN 'Paid' ELSE 'Overdue' END,
            'Rent',
            CONCAT('Monthly rent for ', FORMAT(@PrevInvoiceDate, 'MMMM yyyy'), ' - Unit ', @UnitNumber),
            @PrevInvoiceDate
        );

        SET @InvoicesGenerated = @InvoicesGenerated + 1;
    END

    FETCH NEXT FROM prev_lease_cursor INTO @LeaseId, @TenantId, @MonthlyRent, @PaymentDueDay, @UnitNumber;
END

CLOSE prev_lease_cursor;
DEALLOCATE prev_lease_cursor;

PRINT CONCAT(@InvoicesGenerated, ' invoice(s) generated successfully!');
PRINT '';
PRINT 'Invoice Summary:';
PRINT CONCAT('Total Invoices: ', (SELECT COUNT(*) FROM Invoices));
PRINT CONCAT('Pending: ', (SELECT COUNT(*) FROM Invoices WHERE Status = 'Pending'));
PRINT CONCAT('Paid: ', (SELECT COUNT(*) FROM Invoices WHERE Status = 'Paid'));
PRINT CONCAT('Overdue: ', (SELECT COUNT(*) FROM Invoices WHERE Status = 'Overdue'));
PRINT '';
PRINT CONCAT('Total Amount: KWD ', FORMAT((SELECT SUM(TotalAmount) FROM Invoices), 'N3'));
PRINT CONCAT('Paid Amount: KWD ', FORMAT((SELECT SUM(PaidAmount) FROM Invoices), 'N3'));
PRINT CONCAT('Outstanding: KWD ', FORMAT((SELECT SUM(TotalAmount - PaidAmount) FROM Invoices), 'N3'));

GO
