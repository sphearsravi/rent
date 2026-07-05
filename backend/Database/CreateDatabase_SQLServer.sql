-- =============================================
-- Rent Collection System - Database Creation Script
-- SQL Server 2019+
-- Version: 1.0
-- Date: November 2, 2025
-- =============================================

-- Create Database
USE master;
GO

IF DB_ID('RentCollectionDB') IS NOT NULL
BEGIN
    ALTER DATABASE RentCollectionDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE RentCollectionDB;
END
GO

CREATE DATABASE RentCollectionDB;
GO

USE RentCollectionDB;
GO

-- =============================================
-- Table: Companies
-- =============================================
CREATE TABLE Companies (
    CompanyId INT IDENTITY(1,1) PRIMARY KEY,
    CompanyName NVARCHAR(200) NOT NULL,
    Email NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(50),
    [Address] NVARCHAR(500),
    TaxId NVARCHAR(100),
    LogoUrl NVARCHAR(500),
    SubscriptionType NVARCHAR(50) DEFAULT 'Basic',
    IsActive BIT DEFAULT 1,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    ModifiedDate DATETIME2 NULL
);
GO

CREATE INDEX idx_companies_email ON Companies(Email);
GO

-- =============================================
-- ASP.NET Identity Tables
-- =============================================

-- AspNetRoles
CREATE TABLE AspNetRoles (
    Id NVARCHAR(450) PRIMARY KEY,
    [Name] NVARCHAR(256),
    NormalizedName NVARCHAR(256),
    ConcurrencyStamp NVARCHAR(MAX)
);
GO

CREATE INDEX idx_roles_normalized_name ON AspNetRoles(NormalizedName);
GO

-- AspNetUsers
CREATE TABLE AspNetUsers (
    Id NVARCHAR(450) PRIMARY KEY,
    UserName NVARCHAR(256),
    NormalizedUserName NVARCHAR(256),
    Email NVARCHAR(256),
    NormalizedEmail NVARCHAR(256),
    EmailConfirmed BIT DEFAULT 0,
    PasswordHash NVARCHAR(MAX),
    SecurityStamp NVARCHAR(MAX),
    ConcurrencyStamp NVARCHAR(MAX),
    PhoneNumber NVARCHAR(50),
    PhoneNumberConfirmed BIT DEFAULT 0,
    TwoFactorEnabled BIT DEFAULT 0,
    LockoutEnd DATETIMEOFFSET NULL,
    LockoutEnabled BIT DEFAULT 1,
    AccessFailedCount INT DEFAULT 0,
    -- Custom Fields
    FirstName NVARCHAR(100) NOT NULL,
    LastName NVARCHAR(100) NOT NULL,
    CompanyId INT NULL,
    IsActive BIT DEFAULT 1,
    EmailVerified BIT DEFAULT 0,
    MFAEnabled BIT DEFAULT 0,
    LastLogin DATETIME2 NULL,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    ModifiedDate DATETIME2 NULL,
    CONSTRAINT fk_users_company FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId)
);
GO

CREATE INDEX idx_users_normalized_username ON AspNetUsers(NormalizedUserName);
CREATE INDEX idx_users_normalized_email ON AspNetUsers(NormalizedEmail);
CREATE INDEX idx_users_company ON AspNetUsers(CompanyId);
GO

-- AspNetUserRoles
CREATE TABLE AspNetUserRoles (
    UserId NVARCHAR(450) NOT NULL,
    RoleId NVARCHAR(450) NOT NULL,
    PRIMARY KEY (UserId, RoleId),
    CONSTRAINT fk_user_roles_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE,
    CONSTRAINT fk_user_roles_role FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
);
GO

CREATE INDEX idx_user_roles_role ON AspNetUserRoles(RoleId);
GO

-- AspNetUserClaims
CREATE TABLE AspNetUserClaims (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserId NVARCHAR(450) NOT NULL,
    ClaimType NVARCHAR(MAX),
    ClaimValue NVARCHAR(MAX),
    CONSTRAINT fk_user_claims_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
);
GO

CREATE INDEX idx_user_claims_user ON AspNetUserClaims(UserId);
GO

-- AspNetUserLogins
CREATE TABLE AspNetUserLogins (
    LoginProvider NVARCHAR(450) NOT NULL,
    ProviderKey NVARCHAR(450) NOT NULL,
    ProviderDisplayName NVARCHAR(MAX),
    UserId NVARCHAR(450) NOT NULL,
    PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT fk_user_logins_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
);
GO

CREATE INDEX idx_user_logins_user ON AspNetUserLogins(UserId);
GO

-- AspNetUserTokens
CREATE TABLE AspNetUserTokens (
    UserId NVARCHAR(450) NOT NULL,
    LoginProvider NVARCHAR(450) NOT NULL,
    [Name] NVARCHAR(450) NOT NULL,
    [Value] NVARCHAR(MAX),
    PRIMARY KEY (UserId, LoginProvider, [Name]),
    CONSTRAINT fk_user_tokens_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
);
GO

-- AspNetRoleClaims
CREATE TABLE AspNetRoleClaims (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    RoleId NVARCHAR(450) NOT NULL,
    ClaimType NVARCHAR(MAX),
    ClaimValue NVARCHAR(MAX),
    CONSTRAINT fk_role_claims_role FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
);
GO

CREATE INDEX idx_role_claims_role ON AspNetRoleClaims(RoleId);
GO

-- =============================================
-- Table: Properties
-- =============================================
CREATE TABLE Properties (
    PropertyId INT IDENTITY(1,1) PRIMARY KEY,
    CompanyId INT NOT NULL,
    PropertyName NVARCHAR(200) NOT NULL,
    [Address] NVARCHAR(300) NOT NULL,
    City NVARCHAR(100) NOT NULL,
    [State] NVARCHAR(50) NOT NULL,
    ZipCode NVARCHAR(20) NOT NULL,
    PropertyType NVARCHAR(100) NOT NULL,
    NumberOfUnits INT NOT NULL,
    PurchaseDate DATE NULL,
    PropertyValue DECIMAL(18,2) NULL,
    [Status] NVARCHAR(50) DEFAULT 'Active',
    ManagerId NVARCHAR(450) NULL,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    ModifiedDate DATETIME2 NULL,
    CONSTRAINT fk_properties_company FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId),
    CONSTRAINT fk_properties_manager FOREIGN KEY (ManagerId) REFERENCES AspNetUsers(Id)
);
GO

CREATE INDEX idx_properties_company ON Properties(CompanyId);
CREATE INDEX idx_properties_manager ON Properties(ManagerId);
CREATE INDEX idx_properties_status ON Properties([Status]);
GO

-- =============================================
-- Table: Units
-- =============================================
CREATE TABLE Units (
    UnitId INT IDENTITY(1,1) PRIMARY KEY,
    PropertyId INT NOT NULL,
    UnitNumber NVARCHAR(50) NOT NULL,
    UnitType NVARCHAR(100) NOT NULL,
    SquareFeet INT NULL,
    Bathrooms INT NULL,
    Bedrooms INT NULL,
    [Status] NVARCHAR(50) DEFAULT 'Vacant',
    MonthlyRent DECIMAL(18,2) NOT NULL,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    ModifiedDate DATETIME2 NULL,
    CONSTRAINT fk_units_property FOREIGN KEY (PropertyId) REFERENCES Properties(PropertyId) ON DELETE CASCADE
);
GO

CREATE INDEX idx_units_property ON Units(PropertyId);
CREATE INDEX idx_units_status ON Units([Status]);
CREATE UNIQUE INDEX idx_units_property_number ON Units(PropertyId, UnitNumber);
GO

-- =============================================
-- Table: Tenants
-- =============================================
CREATE TABLE Tenants (
    TenantId INT IDENTITY(1,1) PRIMARY KEY,
    CompanyId INT NOT NULL,
    FirstName NVARCHAR(100) NOT NULL,
    LastName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(50) NOT NULL,
    DateOfBirth DATE NULL,
    GovernmentId NVARCHAR(100) NULL,
    EmploymentStatus NVARCHAR(100) NULL,
    EmergencyContact NVARCHAR(200) NULL,
    EmergencyPhone NVARCHAR(50) NULL,
    [Status] NVARCHAR(50) DEFAULT 'Active',
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    ModifiedDate DATETIME2 NULL,
    CONSTRAINT fk_tenants_company FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId)
);
GO

CREATE INDEX idx_tenants_company ON Tenants(CompanyId);
CREATE INDEX idx_tenants_email ON Tenants(Email);
CREATE INDEX idx_tenants_status ON Tenants([Status]);
GO

-- =============================================
-- Table: Leases
-- =============================================
CREATE TABLE Leases (
    LeaseId INT IDENTITY(1,1) PRIMARY KEY,
    UnitId INT NOT NULL,
    TenantId INT NOT NULL,
    LeaseStartDate DATE NOT NULL,
    LeaseEndDate DATE NOT NULL,
    MonthlyRent DECIMAL(18,2) NOT NULL,
    SecurityDeposit DECIMAL(18,2) NOT NULL,
    PaymentDueDay INT DEFAULT 1,
    LeaseType NVARCHAR(50) DEFAULT 'Fixed',
    [Status] NVARCHAR(50) DEFAULT 'Active',
    LateFeeAmount DECIMAL(18,2) NULL,
    LateFeeGracePeriod INT NULL,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    ModifiedDate DATETIME2 NULL,
    CONSTRAINT fk_leases_unit FOREIGN KEY (UnitId) REFERENCES Units(UnitId),
    CONSTRAINT fk_leases_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId)
);
GO

CREATE INDEX idx_leases_unit ON Leases(UnitId);
CREATE INDEX idx_leases_tenant ON Leases(TenantId);
CREATE INDEX idx_leases_status ON Leases([Status]);
CREATE INDEX idx_leases_end_date ON Leases(LeaseEndDate);
GO

-- =============================================
-- Table: Payments
-- =============================================
CREATE TABLE Payments (
    PaymentId INT IDENTITY(1,1) PRIMARY KEY,
    LeaseId INT NOT NULL,
    TenantId INT NOT NULL,
    PaymentDate DATETIME2 NOT NULL,
    AmountPaid DECIMAL(18,2) NOT NULL,
    PaymentMethod NVARCHAR(100) NOT NULL,
    PaymentStatus NVARCHAR(50) DEFAULT 'Completed',
    TransactionReference NVARCHAR(200) NULL,
    LateFeeAmount DECIMAL(18,2) NULL,
    Notes NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT fk_payments_lease FOREIGN KEY (LeaseId) REFERENCES Leases(LeaseId),
    CONSTRAINT fk_payments_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId)
);
GO

CREATE INDEX idx_payments_lease ON Payments(LeaseId);
CREATE INDEX idx_payments_tenant ON Payments(TenantId);
CREATE INDEX idx_payments_date ON Payments(PaymentDate);
CREATE INDEX idx_payments_status ON Payments(PaymentStatus);
GO

-- =============================================
-- Table: Invoices
-- =============================================
CREATE TABLE Invoices (
    InvoiceId INT IDENTITY(1,1) PRIMARY KEY,
    LeaseId INT NOT NULL,
    TenantId INT NOT NULL,
    InvoiceDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    PaidAmount DECIMAL(18,2) DEFAULT 0,
    [Status] NVARCHAR(50) DEFAULT 'Pending',
    InvoiceType NVARCHAR(50) DEFAULT 'Rent',
    Notes NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT fk_invoices_lease FOREIGN KEY (LeaseId) REFERENCES Leases(LeaseId),
    CONSTRAINT fk_invoices_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId)
);
GO

CREATE INDEX idx_invoices_lease ON Invoices(LeaseId);
CREATE INDEX idx_invoices_tenant ON Invoices(TenantId);
CREATE INDEX idx_invoices_status ON Invoices([Status]);
CREATE INDEX idx_invoices_due_date ON Invoices(DueDate);
GO

-- =============================================
-- Table: MaintenanceRequests
-- =============================================
CREATE TABLE MaintenanceRequests (
    RequestId INT IDENTITY(1,1) PRIMARY KEY,
    PropertyId INT NOT NULL,
    UnitId INT NULL,
    TenantId INT NULL,
    Category NVARCHAR(100) NOT NULL,
    Priority NVARCHAR(50) DEFAULT 'Medium',
    [Description] NVARCHAR(MAX) NOT NULL,
    [Status] NVARCHAR(50) DEFAULT 'Open',
    AssignedToUserId NVARCHAR(450) NULL,
    RequestDate DATETIME2 DEFAULT GETUTCDATE(),
    CompletedDate DATETIME2 NULL,
    EstimatedCost DECIMAL(18,2) NULL,
    ActualCost DECIMAL(18,2) NULL,
    Notes NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 DEFAULT GETUTCDATE(),
    ModifiedDate DATETIME2 NULL,
    CONSTRAINT fk_maintenance_property FOREIGN KEY (PropertyId) REFERENCES Properties(PropertyId),
    CONSTRAINT fk_maintenance_unit FOREIGN KEY (UnitId) REFERENCES Units(UnitId),
    CONSTRAINT fk_maintenance_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId),
    CONSTRAINT fk_maintenance_user FOREIGN KEY (AssignedToUserId) REFERENCES AspNetUsers(Id)
);
GO

CREATE INDEX idx_maintenance_property ON MaintenanceRequests(PropertyId);
CREATE INDEX idx_maintenance_unit ON MaintenanceRequests(UnitId);
CREATE INDEX idx_maintenance_tenant ON MaintenanceRequests(TenantId);
CREATE INDEX idx_maintenance_assigned ON MaintenanceRequests(AssignedToUserId);
CREATE INDEX idx_maintenance_status ON MaintenanceRequests([Status]);
CREATE INDEX idx_maintenance_priority ON MaintenanceRequests(Priority);
GO

-- =============================================
-- Insert Default Roles
-- =============================================
INSERT INTO AspNetRoles (Id, [Name], NormalizedName, ConcurrencyStamp)
VALUES
    (NEWID(), 'SuperAdmin', 'SUPERADMIN', NEWID()),
    (NEWID(), 'CompanyOwner', 'COMPANYOWNER', NEWID()),
    (NEWID(), 'PropertyManager', 'PROPERTYMANAGER', NEWID()),
    (NEWID(), 'Accountant', 'ACCOUNTANT', NEWID()),
    (NEWID(), 'MaintenanceStaff', 'MAINTENANCESTAFF', NEWID()),
    (NEWID(), 'Tenant', 'TENANT', NEWID());
GO

-- =============================================
-- Insert Sample Data (Company, Admin User, Properties, etc.)
-- All in one batch to maintain variable scope
-- =============================================

-- Insert Sample Company
INSERT INTO Companies (CompanyName, Email, Phone, [Address], SubscriptionType, IsActive)
VALUES ('Sample Property Management', 'info@samplepm.com', '555-0100', '123 Business Ave, Suite 100', 'Premium', 1);

DECLARE @CompanyId INT = SCOPE_IDENTITY();

-- Insert Sample Admin User
-- Password: Admin@123
DECLARE @AdminUserId NVARCHAR(450) = CAST(NEWID() AS NVARCHAR(450));
DECLARE @SuperAdminRoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN');

INSERT INTO AspNetUsers (
    Id, UserName, NormalizedUserName, Email, NormalizedEmail,
    EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
    PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled,
    AccessFailedCount, FirstName, LastName, CompanyId, IsActive, EmailVerified
)
VALUES (
    @AdminUserId,
    'admin@rentcollection.com',
    'ADMIN@RENTCOLLECTION.COM',
    'admin@rentcollection.com',
    'ADMIN@RENTCOLLECTION.COM',
    1,
    'AQAAAAIAAYagAAAAEPqKx9fWNvWX8jPx9Cw7gP3gZQ3hOGx5vKL2pQm8NcRv7wHs1tYu6zXa5bDc4eEf3g==',
    CAST(NEWID() AS NVARCHAR(450)),
    CAST(NEWID() AS NVARCHAR(450)),
    '555-0101',
    1,
    0,
    1,
    0,
    'System',
    'Administrator',
    @CompanyId,
    1,
    1
);

INSERT INTO AspNetUserRoles (UserId, RoleId)
VALUES (@AdminUserId, @SuperAdminRoleId);

-- Insert Sample Properties
INSERT INTO Properties (CompanyId, PropertyName, [Address], City, [State], ZipCode, PropertyType, NumberOfUnits, PropertyValue, [Status])
VALUES
    (@CompanyId, 'Sunset Apartments', '456 Sunset Blvd', 'Los Angeles', 'CA', '90001', 'Apartment Complex', 24, 3500000.00, 'Active'),
    (@CompanyId, 'Green Valley Homes', '789 Valley Road', 'San Diego', 'CA', '92101', 'Residential', 12, 2200000.00, 'Active');

-- Get Property IDs
DECLARE @Property1Id INT = (SELECT TOP 1 PropertyId FROM Properties WHERE PropertyName = 'Sunset Apartments');
DECLARE @Property2Id INT = (SELECT TOP 1 PropertyId FROM Properties WHERE PropertyName = 'Green Valley Homes');

-- Insert Sample Units
INSERT INTO Units (PropertyId, UnitNumber, UnitType, SquareFeet, Bathrooms, Bedrooms, [Status], MonthlyRent)
VALUES
    (@Property1Id, '101', '1 Bedroom', 750, 1, 1, 'Occupied', 1800.00),
    (@Property1Id, '102', '2 Bedroom', 1000, 2, 2, 'Vacant', 2200.00),
    (@Property1Id, '103', '2 Bedroom', 1000, 2, 2, 'Occupied', 2200.00),
    (@Property1Id, '201', 'Studio', 500, 1, 0, 'Vacant', 1400.00),
    (@Property2Id, 'A1', '3 Bedroom', 1500, 2, 3, 'Occupied', 3000.00),
    (@Property2Id, 'A2', '2 Bedroom', 1100, 2, 2, 'Occupied', 2400.00);

-- Get Unit IDs
DECLARE @Unit101Id INT = (SELECT TOP 1 UnitId FROM Units WHERE PropertyId = @Property1Id AND UnitNumber = '101');
DECLARE @Unit103Id INT = (SELECT TOP 1 UnitId FROM Units WHERE PropertyId = @Property1Id AND UnitNumber = '103');
DECLARE @UnitA1Id INT = (SELECT TOP 1 UnitId FROM Units WHERE PropertyId = @Property2Id AND UnitNumber = 'A1');

-- Insert Sample Tenants
INSERT INTO Tenants (CompanyId, FirstName, LastName, Email, Phone, DateOfBirth, [Status])
VALUES
    (@CompanyId, 'John', 'Doe', 'john.doe@email.com', '555-0201', '1985-05-15', 'Active'),
    (@CompanyId, 'Jane', 'Smith', 'jane.smith@email.com', '555-0202', '1990-08-22', 'Active'),
    (@CompanyId, 'Michael', 'Johnson', 'michael.j@email.com', '555-0203', '1988-03-10', 'Active');

-- Get Tenant IDs
DECLARE @Tenant1Id INT = (SELECT TOP 1 TenantId FROM Tenants WHERE Email = 'john.doe@email.com');
DECLARE @Tenant2Id INT = (SELECT TOP 1 TenantId FROM Tenants WHERE Email = 'jane.smith@email.com');
DECLARE @Tenant3Id INT = (SELECT TOP 1 TenantId FROM Tenants WHERE Email = 'michael.j@email.com');

-- Insert Sample Leases
INSERT INTO Leases (UnitId, TenantId, LeaseStartDate, LeaseEndDate, MonthlyRent, SecurityDeposit, PaymentDueDay, LeaseType, [Status], LateFeeAmount, LateFeeGracePeriod)
VALUES
    (@Unit101Id, @Tenant1Id, '2024-01-01', '2025-12-31', 1800.00, 3600.00, 1, 'Fixed', 'Active', 50.00, 5),
    (@Unit103Id, @Tenant2Id, '2024-06-01', '2025-05-31', 2200.00, 4400.00, 1, 'Fixed', 'Active', 50.00, 5),
    (@UnitA1Id, @Tenant3Id, '2024-03-01', '2026-02-28', 3000.00, 6000.00, 1, 'Fixed', 'Active', 75.00, 5);

-- Get Lease IDs
DECLARE @Lease1Id INT = (SELECT TOP 1 LeaseId FROM Leases WHERE UnitId = @Unit101Id);
DECLARE @Lease2Id INT = (SELECT TOP 1 LeaseId FROM Leases WHERE UnitId = @Unit103Id);

-- Insert Sample Payments
INSERT INTO Payments (LeaseId, TenantId, PaymentDate, AmountPaid, PaymentMethod, PaymentStatus, TransactionReference)
VALUES
    (@Lease1Id, @Tenant1Id, '2025-10-01 10:30:00', 1800.00, 'Bank Transfer', 'Completed', 'TXN-20251001-001'),
    (@Lease1Id, @Tenant1Id, '2025-11-01 09:15:00', 1800.00, 'Bank Transfer', 'Completed', 'TXN-20251101-001'),
    (@Lease2Id, @Tenant2Id, '2025-10-01 14:20:00', 2200.00, 'Credit Card', 'Completed', 'TXN-20251001-002'),
    (@Lease2Id, @Tenant2Id, '2025-11-01 11:45:00', 2200.00, 'Credit Card', 'Completed', 'TXN-20251101-002');

-- Insert Sample Invoices
INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, [Status], InvoiceType)
VALUES
    (@Lease1Id, @Tenant1Id, '2025-11-25', '2025-12-01', 1800.00, 0.00, 'Pending', 'Rent'),
    (@Lease2Id, @Tenant2Id, '2025-11-25', '2025-12-01', 2200.00, 0.00, 'Pending', 'Rent');

-- Insert Sample Maintenance Requests
INSERT INTO MaintenanceRequests (PropertyId, UnitId, TenantId, Category, Priority, [Description], [Status])
VALUES
    (@Property1Id, @Unit101Id, @Tenant1Id, 'Plumbing', 'High', 'Kitchen sink is leaking', 'Open'),
    (@Property1Id, NULL, NULL, 'Electrical', 'Medium', 'Parking lot lights not working', 'In Progress');
GO

-- =============================================
-- Display Summary
-- =============================================
PRINT 'Database created successfully!';
PRINT '';
SELECT COUNT(*) AS TotalCompanies FROM Companies;
SELECT COUNT(*) AS TotalUsers FROM AspNetUsers;
SELECT COUNT(*) AS TotalRoles FROM AspNetRoles;
SELECT COUNT(*) AS TotalProperties FROM Properties;
SELECT COUNT(*) AS TotalUnits FROM Units;
SELECT COUNT(*) AS TotalTenants FROM Tenants;
SELECT COUNT(*) AS TotalLeases FROM Leases;
SELECT COUNT(*) AS TotalPayments FROM Payments;
SELECT COUNT(*) AS TotalInvoices FROM Invoices;
SELECT COUNT(*) AS TotalMaintenanceRequests FROM MaintenanceRequests;

PRINT '========================================';
PRINT 'Default Admin Credentials:';
PRINT 'Email: admin@rentcollection.com';
PRINT 'Password: Admin@123';
PRINT 'IMPORTANT: Change this password after first login!';
PRINT '========================================';
GO
