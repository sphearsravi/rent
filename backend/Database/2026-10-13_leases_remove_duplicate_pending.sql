-- 2026-10-13 — remove the two duplicate Pending leases created on units that
-- already had an Active lease (units 10 and 11 of "Test 4", leases 721 / 723).
-- Root cause fixed in code (LeasesController.LiveLeaseConflictAsync): a
-- Pending lease never flipped the unit off "Vacant" and wasn't covered by
-- UQ_Leases_UnitId_Active, so a second lease could be created on the unit.
--
-- Guarded: deletes ONLY Pending leases with no invoices, payments, legal
-- cases or documents, on a unit that has an Active lease for the same period.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @victims TABLE (LeaseId int);
INSERT INTO @victims (LeaseId)
SELECT p.LeaseId
FROM Leases p
WHERE p.LeaseId IN (721, 723)
  AND p.Status = 'Pending'
  AND NOT EXISTS (SELECT 1 FROM Invoices   i WHERE i.LeaseId = p.LeaseId)
  AND NOT EXISTS (SELECT 1 FROM Payments   y WHERE y.LeaseId = p.LeaseId)
  AND NOT EXISTS (SELECT 1 FROM LegalCases c WHERE c.LeaseId = p.LeaseId)
  AND NOT EXISTS (SELECT 1 FROM Documents  d WHERE d.RelatedEntityType = 'Lease' AND d.RelatedEntityId = p.LeaseId)
  AND EXISTS (SELECT 1 FROM Leases a
              WHERE a.UnitId = p.UnitId AND a.LeaseId <> p.LeaseId AND a.Status = 'Active'
                AND a.LeaseStartDate <= p.LeaseEndDate AND a.LeaseEndDate >= p.LeaseStartDate);

SELECT 'deleting' AS action, l.LeaseId, l.UnitId, l.TenantId, l.Status, l.MonthlyRent
FROM Leases l JOIN @victims v ON v.LeaseId = l.LeaseId;

DELETE l FROM Leases l JOIN @victims v ON v.LeaseId = l.LeaseId;
PRINT CONCAT('deleted ', @@ROWCOUNT, ' duplicate pending lease(s)');

COMMIT TRANSACTION;

-- Verify: no unit should have more than one live lease for an overlapping period.
SELECT a.UnitId, a.LeaseId, a.Status, b.LeaseId AS OtherLeaseId, b.Status AS OtherStatus
FROM Leases a JOIN Leases b ON a.UnitId = b.UnitId AND a.LeaseId < b.LeaseId
WHERE a.Status IN ('Active','Pending') AND b.Status IN ('Active','Pending')
  AND a.LeaseStartDate <= b.LeaseEndDate AND a.LeaseEndDate >= b.LeaseStartDate;
PRINT 'overlap check done (no rows above = clean)';
