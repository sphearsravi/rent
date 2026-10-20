SET NOCOUNT ON;
PRINT '== ROW COUNTS ==';
SELECT t.name COLLATE DATABASE_DEFAULT + ' = ' + CAST(SUM(p.rows) AS varchar) FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE t.name IN ('Companies','Properties','Units','Tenants','Leases','Invoices','Payments','BankTransfers','CashPayments','Refunds','PaymentAudits','MaintenanceRequests','Documents','DocumentLinks','Notifications','AuditLogs','Users','AspNetUsers','ClosedPeriods','LegalCases','TenantDeviceTokens','PaymentIdempotencies') GROUP BY t.name ORDER BY t.name;
GO
PRINT '== INDEXES on key tables ==';
SELECT t.name COLLATE DATABASE_DEFAULT + '.' + i.name COLLATE DATABASE_DEFAULT + ' (' + STUFF((SELECT ','+c.name COLLATE DATABASE_DEFAULT FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=0 ORDER BY ic.key_ordinal FOR XML PATH('')),1,1,'') + ')' + CASE WHEN i.is_unique=1 THEN ' UNIQUE' ELSE '' END + CASE WHEN i.has_filter=1 THEN ' FILTER' ELSE '' END
FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE i.index_id>0 AND t.name IN ('Invoices','Payments','Leases','Units','Notifications','Refunds','BankTransfers','CashPayments','PaymentAudits','MaintenanceRequests','DocumentLinks') ORDER BY t.name, i.name;
GO
PRINT '== FK delete rules ==';
SELECT fk.name COLLATE DATABASE_DEFAULT + ': ' + OBJECT_NAME(fk.parent_object_id) COLLATE DATABASE_DEFAULT + ' -> ' + OBJECT_NAME(fk.referenced_object_id) COLLATE DATABASE_DEFAULT + ' ON DELETE ' + fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT FROM sys.foreign_keys fk WHERE OBJECT_NAME(fk.referenced_object_id) IN ('Payments','AspNetUsers','Users','Leases','Invoices','Tenants','Properties','Units') ORDER BY 1;
GO
PRINT '== DATA QUALITY ==';
SELECT 'invoices PaidAmount > TotalAmount: ' + CAST(COUNT(*) AS varchar) FROM Invoices WHERE PaidAmount > TotalAmount;
SELECT 'invoices Status=Paid but PaidAmount < TotalAmount: ' + CAST(COUNT(*) AS varchar) FROM Invoices WHERE Status='Paid' AND PaidAmount < TotalAmount;
SELECT 'invoices fully paid but Status<>Paid: ' + CAST(COUNT(*) AS varchar) FROM Invoices WHERE PaidAmount >= TotalAmount AND TotalAmount > 0 AND Status NOT IN ('Paid','Cancelled');
SELECT 'invoices partially paid but Status not Partially Paid: ' + CAST(COUNT(*) AS varchar) FROM Invoices WHERE PaidAmount > 0 AND PaidAmount < TotalAmount AND Status NOT IN ('Partially Paid','Pending Verification','Cancelled','Overdue');
SELECT 'invoices past due & unpaid but not Overdue: ' + CAST(COUNT(*) AS varchar) FROM Invoices WHERE Status IN ('Pending','Partially Paid','Pending Verification') AND DueDate < CAST(GETUTCDATE() AS date) AND TotalAmount-PaidAmount > 0;
SELECT 'invoice statuses: ' + STRING_AGG(Status + '=' + CAST(n AS varchar), ', ') FROM (SELECT Status, COUNT(*) n FROM Invoices GROUP BY Status) s;
SELECT 'invoices w/o lease: ' + CAST(COUNT(*) AS varchar) FROM Invoices i LEFT JOIN Leases l ON l.LeaseId=i.LeaseId WHERE l.LeaseId IS NULL;
SELECT 'payments Completed w/o InvoiceId: ' + CAST(COUNT(*) AS varchar) FROM Payments WHERE Status=2 AND InvoiceId IS NULL;
SELECT 'payments orphan invoice: ' + CAST(COUNT(*) AS varchar) FROM Payments p LEFT JOIN Invoices i ON i.InvoiceId=p.InvoiceId WHERE p.InvoiceId IS NOT NULL AND i.InvoiceId IS NULL;
SELECT 'payments orphan lease: ' + CAST(COUNT(*) AS varchar) FROM Payments p LEFT JOIN Leases l ON l.LeaseId=p.LeaseId WHERE l.LeaseId IS NULL;
SELECT 'payment statuses (int): ' + STRING_AGG(CAST(Status AS varchar) + '=' + CAST(n AS varchar), ', ') FROM (SELECT Status, COUNT(*) n FROM Payments GROUP BY Status) s;
SELECT 'KNET pending > 12h: ' + CAST(COUNT(*) AS varchar) FROM Payments WHERE PaymentType=1 AND Status=1 AND CreatedDate < DATEADD(hour,-12,GETUTCDATE());
SELECT 'active leases on non-Occupied unit: ' + CAST(COUNT(*) AS varchar) FROM Leases l JOIN Units u ON u.UnitId=l.UnitId WHERE l.Status='Active' AND u.Status<>'Occupied';
SELECT 'Occupied units with no active lease: ' + CAST(COUNT(*) AS varchar) FROM Units u WHERE u.Status='Occupied' AND NOT EXISTS (SELECT 1 FROM Leases l WHERE l.UnitId=u.UnitId AND l.Status='Active');
SELECT 'units per status: ' + STRING_AGG(Status + '=' + CAST(n AS varchar), ', ') FROM (SELECT Status, COUNT(*) n FROM Units GROUP BY Status) s;
SELECT 'duplicate ACTIVE leases per unit: ' + CAST(COUNT(*) AS varchar) FROM (SELECT UnitId FROM Leases WHERE Status='Active' GROUP BY UnitId HAVING COUNT(*)>1) d;
SELECT 'Active leases already ended: ' + CAST(COUNT(*) AS varchar) FROM Leases WHERE Status='Active' AND LeaseEndDate < CAST(GETUTCDATE() AS date);
SELECT 'lease statuses: ' + STRING_AGG(Status + '=' + CAST(n AS varchar), ', ') FROM (SELECT Status, COUNT(*) n FROM Leases GROUP BY Status) s;
SELECT 'tenants w/o valid company: ' + CAST(COUNT(*) AS varchar) FROM Tenants t LEFT JOIN Companies c ON c.CompanyId=t.CompanyId WHERE c.CompanyId IS NULL;
SELECT 'properties w/o valid company: ' + CAST(COUNT(*) AS varchar) FROM Properties p LEFT JOIN Companies c ON c.CompanyId=p.CompanyId WHERE c.CompanyId IS NULL;
SELECT 'active tenants with no portal password: ' + CAST(COUNT(*) AS varchar) FROM Tenants WHERE (Password IS NULL OR Password='') AND Status='Active';
SELECT 'tenants duplicate GovernmentId: ' + CAST(COUNT(*) AS varchar) FROM (SELECT GovernmentId FROM Tenants WHERE GovernmentId IS NOT NULL AND GovernmentId<>'' GROUP BY GovernmentId HAVING COUNT(*)>1) d;
SELECT 'documents w/o any link: ' + CAST(COUNT(*) AS varchar) FROM Documents d WHERE NOT EXISTS (SELECT 1 FROM DocumentLinks dl WHERE dl.DocumentId=d.DocumentId);
SELECT 'notifications per status: ' + ISNULL(STRING_AGG(Status + '=' + CAST(n AS varchar), ', '),'none') FROM (SELECT Status, COUNT(*) n FROM Notifications GROUP BY Status) s;
SELECT 'maintenance open past DueDate: ' + CAST(COUNT(*) AS varchar) FROM MaintenanceRequests WHERE Status NOT IN ('Completed','Cancelled') AND DueDate IS NOT NULL AND DueDate < GETUTCDATE();
GO
PRINT '== NULLS in model-non-nullable columns ==';
SELECT 'Units.UnitType null: ' + CAST(SUM(CASE WHEN UnitType IS NULL THEN 1 ELSE 0 END) AS varchar) + ' | UnitNumber null: ' + CAST(SUM(CASE WHEN UnitNumber IS NULL THEN 1 ELSE 0 END) AS varchar) FROM Units;
SELECT 'Leases.LeaseType null: ' + CAST(SUM(CASE WHEN LeaseType IS NULL THEN 1 ELSE 0 END) AS varchar) FROM Leases;
SELECT 'Invoices.InvoiceType null: ' + CAST(SUM(CASE WHEN InvoiceType IS NULL THEN 1 ELSE 0 END) AS varchar) FROM Invoices;
SELECT 'Tenants First/Last null: ' + CAST(SUM(CASE WHEN FirstName IS NULL OR LastName IS NULL THEN 1 ELSE 0 END) AS varchar) + ' | Email null: ' + CAST(SUM(CASE WHEN Email IS NULL THEN 1 ELSE 0 END) AS varchar) + ' | Phone null: ' + CAST(SUM(CASE WHEN Phone IS NULL THEN 1 ELSE 0 END) AS varchar) FROM Tenants;
SELECT 'Properties Address null: ' + CAST(SUM(CASE WHEN Address IS NULL THEN 1 ELSE 0 END) AS varchar) + ' | Governorate null: ' + CAST(SUM(CASE WHEN Governorate IS NULL THEN 1 ELSE 0 END) AS varchar) + ' | PropertyType null: ' + CAST(SUM(CASE WHEN PropertyType IS NULL THEN 1 ELSE 0 END) AS varchar) FROM Properties;
GO
PRINT '== SIZE / FRAGMENTATION ==';
SELECT 'db size MB: ' + CAST(CAST(SUM(size)*8.0/1024 AS decimal(10,1)) AS varchar) FROM sys.database_files;
GO
SELECT TOP 8 OBJECT_NAME(ps.object_id) COLLATE DATABASE_DEFAULT + '.' + i.name COLLATE DATABASE_DEFAULT + ' frag=' + CAST(CAST(ps.avg_fragmentation_in_percent AS decimal(5,1)) AS varchar) + '% pages=' + CAST(ps.page_count AS varchar) FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps JOIN sys.indexes i ON i.object_id=ps.object_id AND i.index_id=ps.index_id WHERE ps.page_count > 50 AND ps.avg_fragmentation_in_percent > 30 ORDER BY ps.avg_fragmentation_in_percent DESC;
GO
PRINT '== MISSING INDEX suggestions ==';
SELECT TOP 8 OBJECT_NAME(d.object_id) COLLATE DATABASE_DEFAULT + ' eq(' + ISNULL(d.equality_columns,'') + ') ineq(' + ISNULL(d.inequality_columns,'') + ') incl(' + ISNULL(d.included_columns,'') + ') impact=' + CAST(CAST(s.avg_user_impact AS int) AS varchar) + ' seeks=' + CAST(s.user_seeks AS varchar) FROM sys.dm_db_missing_index_details d JOIN sys.dm_db_missing_index_groups g ON g.index_handle=d.index_handle JOIN sys.dm_db_missing_index_group_stats s ON s.group_handle=g.index_group_handle WHERE d.database_id=DB_ID() ORDER BY s.avg_user_impact * s.user_seeks DESC;
