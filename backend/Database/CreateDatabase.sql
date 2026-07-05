-- =============================================
-- Rent Collection System - Database Creation Script
-- MySQL 8.0+
-- Version: 1.0
-- Date: November 2, 2025
-- =============================================

-- Create Database
DROP DATABASE IF EXISTS RentCollectionDB;
CREATE DATABASE RentCollectionDB CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE RentCollectionDB;

-- =============================================
-- Table: Companies
-- =============================================
CREATE TABLE Companies (
    CompanyId INT AUTO_INCREMENT PRIMARY KEY,
    CompanyName VARCHAR(200) NOT NULL,
    Email VARCHAR(200) NOT NULL,
    Phone VARCHAR(50),
    Address VARCHAR(500),
    TaxId VARCHAR(100),
    LogoUrl VARCHAR(500),
    SubscriptionType VARCHAR(50) DEFAULT 'Basic',
    IsActive BOOLEAN DEFAULT TRUE,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate DATETIME NULL,
    INDEX idx_companies_email (Email)
) ENGINE=InnoDB;

-- =============================================
-- ASP.NET Identity Tables
-- =============================================

-- AspNetRoles
CREATE TABLE AspNetRoles (
    Id VARCHAR(450) PRIMARY KEY,
    Name VARCHAR(256),
    NormalizedName VARCHAR(256),
    ConcurrencyStamp VARCHAR(500),
    INDEX idx_roles_normalized_name (NormalizedName)
) ENGINE=InnoDB;

-- AspNetUsers
CREATE TABLE AspNetUsers (
    Id VARCHAR(450) PRIMARY KEY,
    UserName VARCHAR(256),
    NormalizedUserName VARCHAR(256),
    Email VARCHAR(256),
    NormalizedEmail VARCHAR(256),
    EmailConfirmed BOOLEAN DEFAULT FALSE,
    PasswordHash TEXT,
    SecurityStamp TEXT,
    ConcurrencyStamp TEXT,
    PhoneNumber VARCHAR(50),
    PhoneNumberConfirmed BOOLEAN DEFAULT FALSE,
    TwoFactorEnabled BOOLEAN DEFAULT FALSE,
    LockoutEnd DATETIME NULL,
    LockoutEnabled BOOLEAN DEFAULT TRUE,
    AccessFailedCount INT DEFAULT 0,
    -- Custom Fields
    FirstName VARCHAR(100) NOT NULL,
    LastName VARCHAR(100) NOT NULL,
    CompanyId INT NULL,
    IsActive BOOLEAN DEFAULT TRUE,
    EmailVerified BOOLEAN DEFAULT FALSE,
    MFAEnabled BOOLEAN DEFAULT FALSE,
    LastLogin DATETIME NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate DATETIME NULL,
    INDEX idx_users_normalized_username (NormalizedUserName),
    INDEX idx_users_normalized_email (NormalizedEmail),
    INDEX idx_users_company (CompanyId),
    CONSTRAINT fk_users_company FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- AspNetUserRoles
CREATE TABLE AspNetUserRoles (
    UserId VARCHAR(450) NOT NULL,
    RoleId VARCHAR(450) NOT NULL,
    PRIMARY KEY (UserId, RoleId),
    INDEX idx_user_roles_role (RoleId),
    CONSTRAINT fk_user_roles_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE,
    CONSTRAINT fk_user_roles_role FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- AspNetUserClaims
CREATE TABLE AspNetUserClaims (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    UserId VARCHAR(450) NOT NULL,
    ClaimType VARCHAR(500),
    ClaimValue TEXT,
    INDEX idx_user_claims_user (UserId),
    CONSTRAINT fk_user_claims_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- AspNetUserLogins
CREATE TABLE AspNetUserLogins (
    LoginProvider VARCHAR(450) NOT NULL,
    ProviderKey VARCHAR(450) NOT NULL,
    ProviderDisplayName VARCHAR(500),
    UserId VARCHAR(450) NOT NULL,
    PRIMARY KEY (LoginProvider, ProviderKey),
    INDEX idx_user_logins_user (UserId),
    CONSTRAINT fk_user_logins_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- AspNetUserTokens
CREATE TABLE AspNetUserTokens (
    UserId VARCHAR(450) NOT NULL,
    LoginProvider VARCHAR(450) NOT NULL,
    Name VARCHAR(450) NOT NULL,
    Value TEXT,
    PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT fk_user_tokens_user FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- AspNetRoleClaims
CREATE TABLE AspNetRoleClaims (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    RoleId VARCHAR(450) NOT NULL,
    ClaimType VARCHAR(500),
    ClaimValue TEXT,
    INDEX idx_role_claims_role (RoleId),
    CONSTRAINT fk_role_claims_role FOREIGN KEY (RoleId) REFERENCES AspNetRoles(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- =============================================
-- Table: Properties
-- =============================================
CREATE TABLE Properties (
    PropertyId INT AUTO_INCREMENT PRIMARY KEY,
    CompanyId INT NOT NULL,
    PropertyName VARCHAR(200) NOT NULL,
    Address VARCHAR(300) NOT NULL,
    City VARCHAR(100) NOT NULL,
    State VARCHAR(50) NOT NULL,
    ZipCode VARCHAR(20) NOT NULL,
    PropertyType VARCHAR(100) NOT NULL,
    NumberOfUnits INT NOT NULL,
    PurchaseDate DATE NULL,
    PropertyValue DECIMAL(18,2) NULL,
    Status VARCHAR(50) DEFAULT 'Active',
    ManagerId VARCHAR(450) NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate DATETIME NULL,
    INDEX idx_properties_company (CompanyId),
    INDEX idx_properties_manager (ManagerId),
    INDEX idx_properties_status (Status),
    CONSTRAINT fk_properties_company FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId) ON DELETE RESTRICT,
    CONSTRAINT fk_properties_manager FOREIGN KEY (ManagerId) REFERENCES AspNetUsers(Id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- =============================================
-- Table: Units
-- =============================================
CREATE TABLE Units (
    UnitId INT AUTO_INCREMENT PRIMARY KEY,
    PropertyId INT NOT NULL,
    UnitNumber VARCHAR(50) NOT NULL,
    UnitType VARCHAR(100) NOT NULL,
    SquareFeet INT NULL,
    Bathrooms INT NULL,
    Bedrooms INT NULL,
    Status VARCHAR(50) DEFAULT 'Vacant',
    MonthlyRent DECIMAL(18,2) NOT NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate DATETIME NULL,
    INDEX idx_units_property (PropertyId),
    INDEX idx_units_status (Status),
    UNIQUE INDEX idx_units_property_number (PropertyId, UnitNumber),
    CONSTRAINT fk_units_property FOREIGN KEY (PropertyId) REFERENCES Properties(PropertyId) ON DELETE CASCADE
) ENGINE=InnoDB;

-- =============================================
-- Table: Tenants
-- =============================================
CREATE TABLE Tenants (
    TenantId INT AUTO_INCREMENT PRIMARY KEY,
    CompanyId INT NOT NULL,
    FirstName VARCHAR(100) NOT NULL,
    LastName VARCHAR(100) NOT NULL,
    Email VARCHAR(200) NOT NULL,
    Phone VARCHAR(50) NOT NULL,
    DateOfBirth DATE NULL,
    GovernmentId VARCHAR(100) NULL,
    EmploymentStatus VARCHAR(100) NULL,
    EmergencyContact VARCHAR(200) NULL,
    EmergencyPhone VARCHAR(50) NULL,
    Status VARCHAR(50) DEFAULT 'Active',
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate DATETIME NULL,
    INDEX idx_tenants_company (CompanyId),
    INDEX idx_tenants_email (Email),
    INDEX idx_tenants_status (Status),
    CONSTRAINT fk_tenants_company FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- =============================================
-- Table: Leases
-- =============================================
CREATE TABLE Leases (
    LeaseId INT AUTO_INCREMENT PRIMARY KEY,
    UnitId INT NOT NULL,
    TenantId INT NOT NULL,
    LeaseStartDate DATE NOT NULL,
    LeaseEndDate DATE NOT NULL,
    MonthlyRent DECIMAL(18,2) NOT NULL,
    SecurityDeposit DECIMAL(18,2) NOT NULL,
    PaymentDueDay INT DEFAULT 1,
    LeaseType VARCHAR(50) DEFAULT 'Fixed',
    Status VARCHAR(50) DEFAULT 'Active',
    LateFeeAmount DECIMAL(18,2) NULL,
    LateFeeGracePeriod INT NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate DATETIME NULL,
    INDEX idx_leases_unit (UnitId),
    INDEX idx_leases_tenant (TenantId),
    INDEX idx_leases_status (Status),
    INDEX idx_leases_end_date (LeaseEndDate),
    CONSTRAINT fk_leases_unit FOREIGN KEY (UnitId) REFERENCES Units(UnitId) ON DELETE RESTRICT,
    CONSTRAINT fk_leases_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- =============================================
-- Table: Payments
-- =============================================
CREATE TABLE Payments (
    PaymentId INT AUTO_INCREMENT PRIMARY KEY,
    LeaseId INT NOT NULL,
    TenantId INT NOT NULL,
    PaymentDate DATETIME NOT NULL,
    AmountPaid DECIMAL(18,2) NOT NULL,
    PaymentMethod VARCHAR(100) NOT NULL,
    PaymentStatus VARCHAR(50) DEFAULT 'Completed',
    TransactionReference VARCHAR(200) NULL,
    LateFeeAmount DECIMAL(18,2) NULL,
    Notes TEXT NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_payments_lease (LeaseId),
    INDEX idx_payments_tenant (TenantId),
    INDEX idx_payments_date (PaymentDate),
    INDEX idx_payments_status (PaymentStatus),
    CONSTRAINT fk_payments_lease FOREIGN KEY (LeaseId) REFERENCES Leases(LeaseId) ON DELETE RESTRICT,
    CONSTRAINT fk_payments_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- =============================================
-- Table: Invoices
-- =============================================
CREATE TABLE Invoices (
    InvoiceId INT AUTO_INCREMENT PRIMARY KEY,
    LeaseId INT NOT NULL,
    TenantId INT NOT NULL,
    InvoiceDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    PaidAmount DECIMAL(18,2) DEFAULT 0,
    Status VARCHAR(50) DEFAULT 'Pending',
    InvoiceType VARCHAR(50) DEFAULT 'Rent',
    Notes TEXT NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_invoices_lease (LeaseId),
    INDEX idx_invoices_tenant (TenantId),
    INDEX idx_invoices_status (Status),
    INDEX idx_invoices_due_date (DueDate),
    CONSTRAINT fk_invoices_lease FOREIGN KEY (LeaseId) REFERENCES Leases(LeaseId) ON DELETE RESTRICT,
    CONSTRAINT fk_invoices_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- =============================================
-- Table: MaintenanceRequests
-- =============================================
CREATE TABLE MaintenanceRequests (
    RequestId INT AUTO_INCREMENT PRIMARY KEY,
    PropertyId INT NOT NULL,
    UnitId INT NULL,
    TenantId INT NULL,
    Category VARCHAR(100) NOT NULL,
    Priority VARCHAR(50) DEFAULT 'Medium',
    Description TEXT NOT NULL,
    Status VARCHAR(50) DEFAULT 'Open',
    AssignedToUserId VARCHAR(450) NULL,
    RequestDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    CompletedDate DATETIME NULL,
    EstimatedCost DECIMAL(18,2) NULL,
    ActualCost DECIMAL(18,2) NULL,
    Notes TEXT NULL,
    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
    ModifiedDate DATETIME NULL,
    INDEX idx_maintenance_property (PropertyId),
    INDEX idx_maintenance_unit (UnitId),
    INDEX idx_maintenance_tenant (TenantId),
    INDEX idx_maintenance_assigned (AssignedToUserId),
    INDEX idx_maintenance_status (Status),
    INDEX idx_maintenance_priority (Priority),
    CONSTRAINT fk_maintenance_property FOREIGN KEY (PropertyId) REFERENCES Properties(PropertyId) ON DELETE RESTRICT,
    CONSTRAINT fk_maintenance_unit FOREIGN KEY (UnitId) REFERENCES Units(UnitId) ON DELETE RESTRICT,
    CONSTRAINT fk_maintenance_tenant FOREIGN KEY (TenantId) REFERENCES Tenants(TenantId) ON DELETE RESTRICT,
    CONSTRAINT fk_maintenance_user FOREIGN KEY (AssignedToUserId) REFERENCES AspNetUsers(Id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- =============================================
-- Insert Default Roles
-- =============================================
INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
VALUES
    (UUID(), 'SuperAdmin', 'SUPERADMIN', UUID()),
    (UUID(), 'CompanyOwner', 'COMPANYOWNER', UUID()),
    (UUID(), 'PropertyManager', 'PROPERTYMANAGER', UUID()),
    (UUID(), 'Accountant', 'ACCOUNTANT', UUID()),
    (UUID(), 'MaintenanceStaff', 'MAINTENANCESTAFF', UUID()),
    (UUID(), 'Tenant', 'TENANT', UUID());

-- =============================================
-- Insert Sample Company
-- =============================================
INSERT INTO Companies (CompanyName, Email, Phone, Address, SubscriptionType, IsActive)
VALUES
    ('Sample Property Management', 'info@samplepm.com', '555-0100', '123 Business Ave, Suite 100', 'Premium', TRUE);

-- =============================================
-- Insert Sample Admin User
-- Note: Password is 'Admin@123' hashed with Identity default hasher
-- You should change this password immediately after first login
-- =============================================
SET @CompanyId = LAST_INSERT_ID();

INSERT INTO AspNetUsers (
    Id, UserName, NormalizedUserName, Email, NormalizedEmail,
    EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
    PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled,
    AccessFailedCount, FirstName, LastName, CompanyId, IsActive, EmailVerified
)
VALUES (
    UUID(),
    'admin@rentcollection.com',
    'ADMIN@RENTCOLLECTION.COM',
    'admin@rentcollection.com',
    'ADMIN@RENTCOLLECTION.COM',
    TRUE,
    'AQAAAAIAAYagAAAAEPqKx9fWNvWX8jPx9Cw7gP3gZQ3hOGx5vKL2pQm8NcRv7wHs1tYu6zXa5bDc4eEf3g==',
    UUID(),
    UUID(),
    '555-0101',
    TRUE,
    FALSE,
    TRUE,
    0,
    'System',
    'Administrator',
    @CompanyId,
    TRUE,
    TRUE
);

SET @AdminUserId = LAST_INSERT_ID();
SET @SuperAdminRoleId = (SELECT Id FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN' LIMIT 1);

INSERT INTO AspNetUserRoles (UserId, RoleId)
VALUES (@AdminUserId, @SuperAdminRoleId);

-- =============================================
-- Insert Sample Properties
-- =============================================
INSERT INTO Properties (CompanyId, PropertyName, Address, City, State, ZipCode, PropertyType, NumberOfUnits, PropertyValue, Status)
VALUES
    (@CompanyId, 'Sunset Apartments', '456 Sunset Blvd', 'Los Angeles', 'CA', '90001', 'Apartment Complex', 24, 3500000.00, 'Active'),
    (@CompanyId, 'Green Valley Homes', '789 Valley Road', 'San Diego', 'CA', '92101', 'Residential', 12, 2200000.00, 'Active');

-- =============================================
-- Insert Sample Units
-- =============================================
SET @Property1Id = (SELECT PropertyId FROM Properties WHERE PropertyName = 'Sunset Apartments' LIMIT 1);
SET @Property2Id = (SELECT PropertyId FROM Properties WHERE PropertyName = 'Green Valley Homes' LIMIT 1);

INSERT INTO Units (PropertyId, UnitNumber, UnitType, SquareFeet, Bathrooms, Bedrooms, Status, MonthlyRent)
VALUES
    (@Property1Id, '101', '1 Bedroom', 750, 1, 1, 'Occupied', 1800.00),
    (@Property1Id, '102', '2 Bedroom', 1000, 2, 2, 'Vacant', 2200.00),
    (@Property1Id, '103', '2 Bedroom', 1000, 2, 2, 'Occupied', 2200.00),
    (@Property1Id, '201', 'Studio', 500, 1, 0, 'Vacant', 1400.00),
    (@Property2Id, 'A1', '3 Bedroom', 1500, 2, 3, 'Occupied', 3000.00),
    (@Property2Id, 'A2', '2 Bedroom', 1100, 2, 2, 'Occupied', 2400.00);

-- =============================================
-- Insert Sample Tenants
-- =============================================
INSERT INTO Tenants (CompanyId, FirstName, LastName, Email, Phone, DateOfBirth, Status)
VALUES
    (@CompanyId, 'John', 'Doe', 'john.doe@email.com', '555-0201', '1985-05-15', 'Active'),
    (@CompanyId, 'Jane', 'Smith', 'jane.smith@email.com', '555-0202', '1990-08-22', 'Active'),
    (@CompanyId, 'Michael', 'Johnson', 'michael.j@email.com', '555-0203', '1988-03-10', 'Active');

-- =============================================
-- Insert Sample Leases
-- =============================================
SET @Unit101Id = (SELECT UnitId FROM Units WHERE PropertyId = @Property1Id AND UnitNumber = '101' LIMIT 1);
SET @Unit103Id = (SELECT UnitId FROM Units WHERE PropertyId = @Property1Id AND UnitNumber = '103' LIMIT 1);
SET @UnitA1Id = (SELECT UnitId FROM Units WHERE PropertyId = @Property2Id AND UnitNumber = 'A1' LIMIT 1);

SET @Tenant1Id = (SELECT TenantId FROM Tenants WHERE Email = 'john.doe@email.com' LIMIT 1);
SET @Tenant2Id = (SELECT TenantId FROM Tenants WHERE Email = 'jane.smith@email.com' LIMIT 1);
SET @Tenant3Id = (SELECT TenantId FROM Tenants WHERE Email = 'michael.j@email.com' LIMIT 1);

INSERT INTO Leases (UnitId, TenantId, LeaseStartDate, LeaseEndDate, MonthlyRent, SecurityDeposit, PaymentDueDay, LeaseType, Status, LateFeeAmount, LateFeeGracePeriod)
VALUES
    (@Unit101Id, @Tenant1Id, '2024-01-01', '2025-12-31', 1800.00, 3600.00, 1, 'Fixed', 'Active', 50.00, 5),
    (@Unit103Id, @Tenant2Id, '2024-06-01', '2025-05-31', 2200.00, 4400.00, 1, 'Fixed', 'Active', 50.00, 5),
    (@UnitA1Id, @Tenant3Id, '2024-03-01', '2026-02-28', 3000.00, 6000.00, 1, 'Fixed', 'Active', 75.00, 5);

-- =============================================
-- Insert Sample Payments
-- =============================================
SET @Lease1Id = (SELECT LeaseId FROM Leases WHERE UnitId = @Unit101Id LIMIT 1);
SET @Lease2Id = (SELECT LeaseId FROM Leases WHERE UnitId = @Unit103Id LIMIT 1);

INSERT INTO Payments (LeaseId, TenantId, PaymentDate, AmountPaid, PaymentMethod, PaymentStatus, TransactionReference)
VALUES
    (@Lease1Id, @Tenant1Id, '2025-10-01 10:30:00', 1800.00, 'Bank Transfer', 'Completed', 'TXN-20251001-001'),
    (@Lease1Id, @Tenant1Id, '2025-11-01 09:15:00', 1800.00, 'Bank Transfer', 'Completed', 'TXN-20251101-001'),
    (@Lease2Id, @Tenant2Id, '2025-10-01 14:20:00', 2200.00, 'Credit Card', 'Completed', 'TXN-20251001-002'),
    (@Lease2Id, @Tenant2Id, '2025-11-01 11:45:00', 2200.00, 'Credit Card', 'Completed', 'TXN-20251101-002');

-- =============================================
-- Insert Sample Invoices
-- =============================================
INSERT INTO Invoices (LeaseId, TenantId, InvoiceDate, DueDate, TotalAmount, PaidAmount, Status, InvoiceType)
VALUES
    (@Lease1Id, @Tenant1Id, '2025-11-25', '2025-12-01', 1800.00, 0.00, 'Pending', 'Rent'),
    (@Lease2Id, @Tenant2Id, '2025-11-25', '2025-12-01', 2200.00, 0.00, 'Pending', 'Rent');

-- =============================================
-- Insert Sample Maintenance Requests
-- =============================================
INSERT INTO MaintenanceRequests (PropertyId, UnitId, TenantId, Category, Priority, Description, Status)
VALUES
    (@Property1Id, @Unit101Id, @Tenant1Id, 'Plumbing', 'High', 'Kitchen sink is leaking', 'Open'),
    (@Property1Id, NULL, NULL, 'Electrical', 'Medium', 'Parking lot lights not working', 'In Progress');

-- =============================================
-- Display Summary
-- =============================================
SELECT 'Database created successfully!' AS Status;
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

SELECT '========================================' AS '';
SELECT 'Default Admin Credentials:' AS '';
SELECT 'Email: admin@rentcollection.com' AS '';
SELECT 'Password: Admin@123' AS '';
SELECT 'IMPORTANT: Change this password after first login!' AS '';
SELECT '========================================' AS '';
