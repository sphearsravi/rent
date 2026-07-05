-- Baseline seed for the E2E workflow (CI). Creates the single company and the
-- admin the Playwright AdminSession signs in as (admin@test.com / Admin@123).
-- The PasswordHash below is the app's own salt:hash for "Admin@123" (copied
-- from the dev DB), so /Account/Login verifies it as-is. The seeded TENANT is
-- created at test-time by TenantSeedFixture, so it is not seeded here.

SET NOCOUNT ON;

-- Company #1 (FK target for the admin + all test-created properties/tenants).
SET IDENTITY_INSERT Companies ON;
IF NOT EXISTS (SELECT 1 FROM Companies WHERE CompanyId = 1)
    INSERT INTO Companies (CompanyId, CompanyName, Email)
    VALUES (1, 'Alratm', 'company@example.com');
SET IDENTITY_INSERT Companies OFF;

-- Admin user linked to company 1 (so tenant-create resolves a company).
IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'admin@test.com')
    INSERT INTO Users (Username, Email, PasswordHash, FirstName, LastName, Role,
                       IsActive, EmailVerified, CreatedDate, MfaEnabled, CompanyId)
    VALUES ('admin', 'admin@test.com',
            'MHxX8uwvfJkTWVqSRk4Uaw==:IukmZMLKP3gTIM2pyQ3d5j6B2R0hpch2VQWKGiZDL2E=',
            'Admin', 'User', 'Admin', 1, 1, GETUTCDATE(), 0, 1);

SELECT 'seed-e2e applied: companies=' + CAST((SELECT COUNT(*) FROM Companies WHERE CompanyId = 1) AS varchar)
     + ' admin=' + CAST((SELECT COUNT(*) FROM Users WHERE Email = 'admin@test.com') AS varchar);
