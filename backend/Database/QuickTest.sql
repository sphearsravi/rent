-- Quick test to verify database was created successfully
USE RentCollectionDB;
GO

-- Check if all tables exist
SELECT 'Tables Created' AS Status;
SELECT name AS TableName
FROM sys.tables
WHERE type = 'U'
ORDER BY name;

-- Check sample data counts
SELECT 'Data Summary' AS Status;
SELECT
    (SELECT COUNT(*) FROM Companies) AS Companies,
    (SELECT COUNT(*) FROM AspNetUsers) AS Users,
    (SELECT COUNT(*) FROM AspNetRoles) AS Roles,
    (SELECT COUNT(*) FROM Properties) AS Properties,
    (SELECT COUNT(*) FROM Units) AS Units,
    (SELECT COUNT(*) FROM Tenants) AS Tenants,
    (SELECT COUNT(*) FROM Leases) AS Leases,
    (SELECT COUNT(*) FROM Payments) AS Payments,
    (SELECT COUNT(*) FROM Invoices) AS Invoices,
    (SELECT COUNT(*) FROM MaintenanceRequests) AS Maintenance;

-- Show admin user
SELECT 'Admin User' AS Info;
SELECT
    u.UserName,
    u.Email,
    u.FirstName,
    u.LastName,
    r.Name AS RoleName
FROM AspNetUsers u
LEFT JOIN AspNetUserRoles ur ON u.Id = ur.UserId
LEFT JOIN AspNetRoles r ON ur.RoleId = r.Id
WHERE u.Email = 'admin@rentcollection.com';

-- Show properties
SELECT 'Properties' AS Info;
SELECT PropertyId, PropertyName, City, NumberOfUnits, Status
FROM Properties;

-- Show units with status
SELECT 'Units' AS Info;
SELECT
    p.PropertyName,
    u.UnitNumber,
    u.UnitType,
    u.Status,
    u.MonthlyRent
FROM Units u
JOIN Properties p ON u.PropertyId = p.PropertyId
ORDER BY p.PropertyName, u.UnitNumber;

-- Show tenants with leases
SELECT 'Active Tenants' AS Info;
SELECT
    t.FirstName + ' ' + t.LastName AS TenantName,
    p.PropertyName,
    u.UnitNumber,
    l.LeaseStartDate,
    l.LeaseEndDate,
    l.MonthlyRent
FROM Tenants t
JOIN Leases l ON t.TenantId = l.TenantId
JOIN Units u ON l.UnitId = u.UnitId
JOIN Properties p ON u.PropertyId = p.PropertyId
WHERE l.Status = 'Active';

PRINT 'Database verification complete!';
PRINT 'If you see data above, the database was created successfully.';
