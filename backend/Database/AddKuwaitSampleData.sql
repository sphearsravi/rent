-- Add comprehensive Kuwait sample data for Rent Collection System
USE RentCollectionDB;
GO

-- First, run the PACI migration if not already done
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = 'PACINumber')
BEGIN
    ALTER TABLE Properties ADD PACINumber NVARCHAR(50) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND name = 'PACINumber')
BEGIN
    ALTER TABLE Tenants ADD PACINumber NVARCHAR(50) NULL;
END
GO

-- Clear existing sample data (keep structure)
DELETE FROM Payments;
DELETE FROM Invoices;
DELETE FROM MaintenanceRequests;
DELETE FROM Leases;
DELETE FROM Units;
DELETE FROM Tenants;
DELETE FROM Properties;
GO

-- Insert Kuwait Properties
DECLARE @CompanyId INT = (SELECT TOP 1 CompanyId FROM Companies);

INSERT INTO Properties (CompanyId, PropertyName, PACINumber, Address, City, State, ZipCode, PropertyType, NumberOfUnits, PurchaseDate, PropertyValue, Status, CreatedDate)
VALUES
(@CompanyId, 'Salmiya Heights', '789456123', 'Salem Al Mubarak Street', 'Salmiya', 'Hawalli', '22000', 'Apartment', 24, '2020-03-15', 450000.000, 'Active', GETDATE()),
(@CompanyId, 'Fintas Towers', '987654321', 'Coastal Road', 'Fintas', 'Ahmadi', '51500', 'Apartment', 30, '2019-06-20', 680000.000, 'Active', GETDATE()),
(@CompanyId, 'Jabriya Complex', '456789123', 'Block 5 Street 12', 'Jabriya', 'Hawalli', '46300', 'Apartment', 18, '2021-01-10', 320000.000, 'Active', GETDATE()),
(@CompanyId, 'Sharq Business Center', '147258369', 'Arabian Gulf Street', 'Sharq', 'Capital', '15300', 'Commercial', 12, '2018-11-05', 950000.000, 'Active', GETDATE()),
(@CompanyId, 'Mangaf Residences', '369258147', 'Fahaheel Expressway', 'Mangaf', 'Ahmadi', '53500', 'Apartment', 20, '2022-02-28', 385000.000, 'Active', GETDATE());

-- Get Property IDs
DECLARE @SalmiyaId INT = (SELECT PropertyId FROM Properties WHERE PropertyName = 'Salmiya Heights');
DECLARE @FintasId INT = (SELECT PropertyId FROM Properties WHERE PropertyName = 'Fintas Towers');
DECLARE @JabriyaId INT = (SELECT PropertyId FROM Properties WHERE PropertyName = 'Jabriya Complex');
DECLARE @SharqId INT = (SELECT PropertyId FROM Properties WHERE PropertyName = 'Sharq Business Center');
DECLARE @MangafId INT = (SELECT PropertyId FROM Properties WHERE PropertyName = 'Mangaf Residences');

-- Insert Units
INSERT INTO Units (PropertyId, UnitNumber, UnitType, Bedrooms, Bathrooms, SquareFeet, MonthlyRent, Status, CreatedDate)
VALUES
-- Salmiya Heights Units
(@SalmiyaId, 'A101', 'Apartment', 2, 2, 1200, 380.000, 'Occupied', GETDATE()),
(@SalmiyaId, 'A102', 'Apartment', 2, 2, 1200, 380.000, 'Occupied', GETDATE()),
(@SalmiyaId, 'A201', 'Apartment', 3, 2, 1450, 450.000, 'Occupied', GETDATE()),
(@SalmiyaId, 'A202', 'Apartment', 3, 2, 1450, 450.000, 'Vacant', GETDATE()),
(@SalmiyaId, 'A301', 'Apartment', 3, 3, 1600, 520.000, 'Occupied', GETDATE()),
(@SalmiyaId, 'A401', 'Penthouse', 4, 3, 2200, 750.000, 'Vacant', GETDATE()),

-- Fintas Towers Units
(@FintasId, 'T1-101', 'Apartment', 2, 2, 1100, 350.000, 'Occupied', GETDATE()),
(@FintasId, 'T1-201', 'Apartment', 2, 2, 1100, 350.000, 'Occupied', GETDATE()),
(@FintasId, 'T1-301', 'Apartment', 3, 2, 1400, 420.000, 'Occupied', GETDATE()),
(@FintasId, 'T2-101', 'Apartment', 2, 2, 1150, 360.000, 'Vacant', GETDATE()),
(@FintasId, 'T2-201', 'Apartment', 3, 2, 1350, 410.000, 'Occupied', GETDATE()),

-- Jabriya Complex Units
(@JabriyaId, 'J-101', 'Apartment', 2, 1, 950, 300.000, 'Occupied', GETDATE()),
(@JabriyaId, 'J-102', 'Apartment', 2, 1, 950, 300.000, 'Occupied', GETDATE()),
(@JabriyaId, 'J-201', 'Apartment', 3, 2, 1250, 380.000, 'Occupied', GETDATE()),
(@JabriyaId, 'J-202', 'Apartment', 3, 2, 1250, 380.000, 'Vacant', GETDATE()),

-- Sharq Business Center Units
(@SharqId, 'Office-101', 'Commercial', 0, 2, 800, 600.000, 'Occupied', GETDATE()),
(@SharqId, 'Office-201', 'Commercial', 0, 2, 800, 600.000, 'Occupied', GETDATE()),
(@SharqId, 'Office-301', 'Commercial', 0, 2, 1000, 750.000, 'Occupied', GETDATE()),
(@SharqId, 'Office-401', 'Commercial', 0, 1, 600, 480.000, 'Vacant', GETDATE()),

-- Mangaf Residences Units
(@MangafId, 'M-101', 'Apartment', 2, 2, 1100, 340.000, 'Occupied', GETDATE()),
(@MangafId, 'M-102', 'Apartment', 2, 2, 1100, 340.000, 'Occupied', GETDATE()),
(@MangafId, 'M-201', 'Apartment', 3, 2, 1350, 420.000, 'Occupied', GETDATE()),
(@MangafId, 'M-301', 'Apartment', 3, 2, 1350, 420.000, 'Vacant', GETDATE());

-- Insert Tenants
INSERT INTO Tenants (CompanyId, FirstName, LastName, PACINumber, Email, Phone, DateOfBirth, GovernmentId, EmploymentStatus, EmergencyContact, EmergencyPhone, Status, CreatedDate)
VALUES
(@CompanyId, 'Ahmed', 'Al-Rashid', '265123456789', 'ahmed.rashid@email.com', '+965-9988-7766', '1985-04-12', 'ID-265123456', 'Employed', 'Fatima Al-Rashid', '+965-9988-7767', 'Active', GETDATE()),
(@CompanyId, 'Sarah', 'Al-Mutairi', '265234567890', 'sarah.mutairi@email.com', '+965-9977-6655', '1990-08-25', 'ID-265234567', 'Self-Employed', 'Mohammed Al-Mutairi', '+965-9977-6656', 'Active', GETDATE()),
(@CompanyId, 'Mohammed', 'Al-Sabah', '265345678901', 'mohammed.sabah@email.com', '+965-9966-5544', '1988-02-18', 'ID-265345678', 'Employed', 'Aisha Al-Sabah', '+965-9966-5545', 'Active', GETDATE()),
(@CompanyId, 'Fatima', 'Al-Ahmad', '265456789012', 'fatima.ahmad@email.com', '+965-9955-4433', '1992-11-30', 'ID-265456789', 'Employed', 'Hassan Al-Ahmad', '+965-9955-4434', 'Active', GETDATE()),
(@CompanyId, 'Khalid', 'Al-Faris', '265567890123', 'khalid.faris@email.com', '+965-9944-3322', '1987-06-15', 'ID-265567890', 'Business Owner', 'Noura Al-Faris', '+965-9944-3323', 'Active', GETDATE()),
(@CompanyId, 'Layla', 'Al-Hassan', '265678901234', 'layla.hassan@email.com', '+965-9933-2211', '1991-03-22', 'ID-265678901', 'Employed', 'Yousef Al-Hassan', '+965-9933-2212', 'Active', GETDATE()),
(@CompanyId, 'Omar', 'Al-Zaid', '265789012345', 'omar.zaid@email.com', '+965-9922-1100', '1989-09-08', 'ID-265789012', 'Employed', 'Maryam Al-Zaid', '+965-9922-1101', 'Active', GETDATE()),
(@CompanyId, 'Noura', 'Al-Kandari', '265890123456', 'noura.kandari@email.com', '+965-9911-0099', '1993-07-14', 'ID-265890123', 'Self-Employed', 'Abdullah Al-Kandari', '+965-9911-0098', 'Active', GETDATE()),
(@CompanyId, 'TechCorp Kuwait', 'Limited', '265901234567', 'info@techcorp.com.kw', '+965-2222-3333', NULL, 'CR-265901234', 'Corporate', 'Ali Al-Mansour', '+965-2222-3334', 'Active', GETDATE()),
(@CompanyId, 'Green Solutions', 'LLC', '265012345678', 'contact@greensolutions.kw', '+965-2233-4455', NULL, 'CR-265012345', 'Corporate', 'Hind Al-Sabah', '+965-2233-4456', 'Active', GETDATE()),
(@CompanyId, 'Consulting Plus', 'WLL', '265123456780', 'office@consultingplus.kw', '+965-2244-5566', NULL, 'CR-265123456', 'Corporate', 'Jassim Al-Kharafi', '+965-2244-5567', 'Active', GETDATE());

-- Get Tenant IDs
DECLARE @AhmedId INT = (SELECT TenantId FROM Tenants WHERE Email = 'ahmed.rashid@email.com');
DECLARE @SarahId INT = (SELECT TenantId FROM Tenants WHERE Email = 'sarah.mutairi@email.com');
DECLARE @MohammedId INT = (SELECT TenantId FROM Tenants WHERE Email = 'mohammed.sabah@email.com');
DECLARE @FatimaId INT = (SELECT TenantId FROM Tenants WHERE Email = 'fatima.ahmad@email.com');
DECLARE @KhalidId INT = (SELECT TenantId FROM Tenants WHERE Email = 'khalid.faris@email.com');
DECLARE @LaylaId INT = (SELECT TenantId FROM Tenants WHERE Email = 'layla.hassan@email.com');
DECLARE @OmarId INT = (SELECT TenantId FROM Tenants WHERE Email = 'omar.zaid@email.com');
DECLARE @NouraId INT = (SELECT TenantId FROM Tenants WHERE Email = 'noura.kandari@email.com');
DECLARE @TechCorpId INT = (SELECT TenantId FROM Tenants WHERE FirstName = 'TechCorp Kuwait');
DECLARE @GreenId INT = (SELECT TenantId FROM Tenants WHERE FirstName = 'Green Solutions');
DECLARE @ConsultingId INT = (SELECT TenantId FROM Tenants WHERE FirstName = 'Consulting Plus');

-- Get Unit IDs
DECLARE @UnitA101 INT = (SELECT UnitId FROM Units WHERE PropertyId = @SalmiyaId AND UnitNumber = 'A101');
DECLARE @UnitA102 INT = (SELECT UnitId FROM Units WHERE PropertyId = @SalmiyaId AND UnitNumber = 'A102');
DECLARE @UnitA201 INT = (SELECT UnitId FROM Units WHERE PropertyId = @SalmiyaId AND UnitNumber = 'A201');
DECLARE @UnitA301 INT = (SELECT UnitId FROM Units WHERE PropertyId = @SalmiyaId AND UnitNumber = 'A301');
DECLARE @UnitT1_101 INT = (SELECT UnitId FROM Units WHERE PropertyId = @FintasId AND UnitNumber = 'T1-101');
DECLARE @UnitT1_201 INT = (SELECT UnitId FROM Units WHERE PropertyId = @FintasId AND UnitNumber = 'T1-201');
DECLARE @UnitT1_301 INT = (SELECT UnitId FROM Units WHERE PropertyId = @FintasId AND UnitNumber = 'T1-301');
DECLARE @UnitT2_201 INT = (SELECT UnitId FROM Units WHERE PropertyId = @FintasId AND UnitNumber = 'T2-201');
DECLARE @UnitJ101 INT = (SELECT UnitId FROM Units WHERE PropertyId = @JabriyaId AND UnitNumber = 'J-101');
DECLARE @UnitJ102 INT = (SELECT UnitId FROM Units WHERE PropertyId = @JabriyaId AND UnitNumber = 'J-102');
DECLARE @UnitJ201 INT = (SELECT UnitId FROM Units WHERE PropertyId = @JabriyaId AND UnitNumber = 'J-201');
DECLARE @UnitOffice101 INT = (SELECT UnitId FROM Units WHERE PropertyId = @SharqId AND UnitNumber = 'Office-101');
DECLARE @UnitOffice201 INT = (SELECT UnitId FROM Units WHERE PropertyId = @SharqId AND UnitNumber = 'Office-201');
DECLARE @UnitOffice301 INT = (SELECT UnitId FROM Units WHERE PropertyId = @SharqId AND UnitNumber = 'Office-301');
DECLARE @UnitM101 INT = (SELECT UnitId FROM Units WHERE PropertyId = @MangafId AND UnitNumber = 'M-101');
DECLARE @UnitM102 INT = (SELECT UnitId FROM Units WHERE PropertyId = @MangafId AND UnitNumber = 'M-102');
DECLARE @UnitM201 INT = (SELECT UnitId FROM Units WHERE PropertyId = @MangafId AND UnitNumber = 'M-201');

-- Insert Leases (showing tenants can have multiple leases)
INSERT INTO Leases (UnitId, TenantId, LeaseStartDate, LeaseEndDate, MonthlyRent, SecurityDeposit, PaymentDueDay, LeaseType, Status, LateFeeAmount, LateFeeGracePeriod, CreatedDate)
VALUES
-- Active Leases
(@UnitA101, @AhmedId, '2024-01-01', '2024-12-31', 380.000, 760.000, 1, 'Fixed', 'Active', 15.000, 5, GETDATE()),
(@UnitA102, @SarahId, '2024-02-01', '2025-01-31', 380.000, 760.000, 1, 'Fixed', 'Active', 15.000, 5, GETDATE()),
(@UnitA201, @MohammedId, '2023-12-01', '2024-11-30', 450.000, 900.000, 5, 'Fixed', 'Active', 20.000, 5, GETDATE()),
(@UnitA301, @FatimaId, '2024-03-01', '2025-02-28', 520.000, 1040.000, 1, 'Fixed', 'Active', 25.000, 5, GETDATE()),
(@UnitT1_101, @KhalidId, '2024-01-15', '2025-01-14', 350.000, 700.000, 15, 'Fixed', 'Active', 15.000, 5, GETDATE()),
(@UnitT1_201, @LaylaId, '2024-02-01', '2025-01-31', 350.000, 700.000, 1, 'Fixed', 'Active', 15.000, 5, GETDATE()),
(@UnitT1_301, @OmarId, '2023-11-01', '2024-10-31', 420.000, 840.000, 1, 'Fixed', 'Active', 18.000, 5, GETDATE()),
(@UnitT2_201, @NouraId, '2024-03-01', '2025-02-28', 410.000, 820.000, 1, 'Fixed', 'Active', 18.000, 5, GETDATE()),
(@UnitJ101, @SarahId, '2024-01-01', '2024-12-31', 300.000, 600.000, 1, 'Fixed', 'Active', 12.000, 5, GETDATE()),
(@UnitJ102, @AhmedId, '2024-02-01', '2025-01-31', 300.000, 600.000, 1, 'Fixed', 'Active', 12.000, 5, GETDATE()),
(@UnitJ201, @KhalidId, '2024-01-01', '2024-12-31', 380.000, 760.000, 5, 'Fixed', 'Active', 15.000, 5, GETDATE()),
(@UnitOffice101, @TechCorpId, '2023-06-01', '2025-05-31', 600.000, 1800.000, 1, 'Fixed', 'Active', 30.000, 3, GETDATE()),
(@UnitOffice201, @GreenId, '2023-09-01', '2025-08-31', 600.000, 1800.000, 1, 'Fixed', 'Active', 30.000, 3, GETDATE()),
(@UnitOffice301, @ConsultingId, '2024-01-01', '2025-12-31', 750.000, 2250.000, 1, 'Fixed', 'Active', 35.000, 3, GETDATE()),
(@UnitM101, @MohammedId, '2024-02-01', '2025-01-31', 340.000, 680.000, 1, 'Fixed', 'Active', 15.000, 5, GETDATE()),
(@UnitM102, @FatimaId, '2024-01-15', '2025-01-14', 340.000, 680.000, 15, 'Fixed', 'Active', 15.000, 5, GETDATE()),
(@UnitM201, @LaylaId, '2024-03-01', '2025-02-28', 420.000, 840.000, 1, 'Fixed', 'Active', 18.000, 5, GETDATE());

-- Insert Payments (last 3 months for active leases)
DECLARE @LeaseId INT;

-- Payments for Ahmed (A101)
SET @LeaseId = (SELECT LeaseId FROM Leases WHERE UnitId = @UnitA101 AND TenantId = @AhmedId);
INSERT INTO Payments (LeaseId, TenantId, PaymentDate, AmountPaid, PaymentMethod, PaymentStatus, TransactionReference, CreatedDate)
VALUES
(@LeaseId, @AhmedId, '2024-08-01', 380.000, 'Bank Transfer', 'Completed', 'TXN-2024080001', DATEADD(day, -60, GETDATE())),
(@LeaseId, @AhmedId, '2024-09-01', 380.000, 'Bank Transfer', 'Completed', 'TXN-2024090001', DATEADD(day, -30, GETDATE())),
(@LeaseId, @AhmedId, '2024-10-01', 380.000, 'Bank Transfer', 'Completed', 'TXN-2024100001', GETDATE());

-- Payments for Sarah (A102 and J101 - multiple properties)
SET @LeaseId = (SELECT LeaseId FROM Leases WHERE UnitId = @UnitA102 AND TenantId = @SarahId);
INSERT INTO Payments (LeaseId, TenantId, PaymentDate, AmountPaid, PaymentMethod, PaymentStatus, TransactionReference, CreatedDate)
VALUES
(@LeaseId, @SarahId, '2024-08-01', 380.000, 'Credit Card', 'Completed', 'TXN-2024080002', DATEADD(day, -60, GETDATE())),
(@LeaseId, @SarahId, '2024-09-01', 380.000, 'Credit Card', 'Completed', 'TXN-2024090002', DATEADD(day, -30, GETDATE())),
(@LeaseId, @SarahId, '2024-10-01', 380.000, 'Credit Card', 'Completed', 'TXN-2024100002', GETDATE());

SET @LeaseId = (SELECT LeaseId FROM Leases WHERE UnitId = @UnitJ101 AND TenantId = @SarahId);
INSERT INTO Payments (LeaseId, TenantId, PaymentDate, AmountPaid, PaymentMethod, PaymentStatus, TransactionReference, CreatedDate)
VALUES
(@LeaseId, @SarahId, '2024-08-01', 300.000, 'Credit Card', 'Completed', 'TXN-2024080003', DATEADD(day, -60, GETDATE())),
(@LeaseId, @SarahId, '2024-09-01', 300.000, 'Credit Card', 'Completed', 'TXN-2024090003', DATEADD(day, -30, GETDATE())),
(@LeaseId, @SarahId, '2024-10-01', 300.000, 'Credit Card', 'Completed', 'TXN-2024100003', GETDATE());

-- More payments for other tenants
SET @LeaseId = (SELECT LeaseId FROM Leases WHERE UnitId = @UnitOffice101 AND TenantId = @TechCorpId);
INSERT INTO Payments (LeaseId, TenantId, PaymentDate, AmountPaid, PaymentMethod, PaymentStatus, TransactionReference, CreatedDate)
VALUES
(@LeaseId, @TechCorpId, '2024-08-01', 600.000, 'Bank Transfer', 'Completed', 'TXN-2024080010', DATEADD(day, -60, GETDATE())),
(@LeaseId, @TechCorpId, '2024-09-01', 600.000, 'Bank Transfer', 'Completed', 'TXN-2024090010', DATEADD(day, -30, GETDATE())),
(@LeaseId, @TechCorpId, '2024-10-01', 600.000, 'Bank Transfer', 'Completed', 'TXN-2024100010', GETDATE());

-- Insert Maintenance Requests
DECLARE @AdminUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers);

INSERT INTO MaintenanceRequests (PropertyId, UnitId, TenantId, Category, Priority, Description, Status, AssignedToUserId, RequestDate, CompletedDate, ActualCost)
VALUES
(@SalmiyaId, @UnitA101, @AhmedId, 'Plumbing', 'High', 'Kitchen sink is leaking badly', 'Completed', @AdminUserId, DATEADD(day, -15, GETDATE()), DATEADD(day, -13, GETDATE()), 45.500),
(@SalmiyaId, @UnitA102, @SarahId, 'Electrical', 'Medium', 'Living room light fixture not working', 'Completed', @AdminUserId, DATEADD(day, -10, GETDATE()), DATEADD(day, -8, GETDATE()), 32.000),
(@FintasId, @UnitT1_101, @KhalidId, 'HVAC', 'High', 'Air conditioning not cooling properly', 'In Progress', @AdminUserId, DATEADD(day, -5, GETDATE()), NULL, NULL),
(@JabriyaId, @UnitJ201, @KhalidId, 'Appliance', 'Low', 'Refrigerator making strange noise', 'Open', @AdminUserId, DATEADD(day, -2, GETDATE()), NULL, NULL),
(@SharqId, @UnitOffice101, @TechCorpId, 'General', 'Medium', 'Office door lock needs replacement', 'In Progress', @AdminUserId, DATEADD(day, -7, GETDATE()), NULL, NULL),
(@MangafId, @UnitM201, @LaylaId, 'Plumbing', 'High', 'Bathroom shower has low water pressure', 'Open', @AdminUserId, DATEADD(day, -3, GETDATE()), NULL, NULL);

PRINT 'Kuwait sample data added successfully!';
PRINT '';
PRINT 'Summary:';
PRINT '- 5 Properties (Salmiya, Fintas, Jabriya, Sharq, Mangaf)';
PRINT '- 23 Units (Apartments and Commercial spaces)';
PRINT '- 11 Tenants (including 3 companies, showing multiple leases)';
PRINT '- 17 Active Leases (demonstrating tenants with multiple properties)';
PRINT '- Multiple Payments (showing payment history)';
PRINT '- 6 Maintenance Requests (various statuses)';
PRINT '';
PRINT 'Note: Sarah has 2 leases (A102 + J101), Ahmed has 2 leases (A101 + J102),';
PRINT '      Khalid has 2 leases (T1-101 + J-201), demonstrating multiple leases per tenant';
GO
