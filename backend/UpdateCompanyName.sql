-- ========================================
-- Update Company Name from "Sample Property Management" to "Alratm"
-- ========================================

USE RentCollectionDB;
GO

PRINT 'Updating company name...';

UPDATE Companies
SET CompanyName = 'Alratm'
WHERE CompanyName = 'Sample Property Management';

PRINT '✓ Company name updated successfully!';
PRINT '';

-- Display updated company information
SELECT
    CompanyId,
    CompanyName,
    Email,
    Phone,
    Address
FROM Companies;
GO
