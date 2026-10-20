-- LookupItems: admin-managed dropdown values (classification categories) with
-- English + Arabic labels. Applied out-of-band (idempotent) because this DB's
-- schema has diverged from the EF snapshot, so `ef database update` is unsafe.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LookupItems')
BEGIN
    CREATE TABLE dbo.LookupItems
    (
        LookupItemId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LookupItems PRIMARY KEY,
        Category      NVARCHAR(50)  NOT NULL,
        Code          NVARCHAR(100) NOT NULL,
        LabelEn       NVARCHAR(200) NULL,
        LabelAr       NVARCHAR(200) NULL,
        SortOrder     INT           NOT NULL CONSTRAINT DF_LookupItems_Sort DEFAULT(0),
        IsActive      BIT           NOT NULL CONSTRAINT DF_LookupItems_Active DEFAULT(1),
        CompanyId     INT           NULL,
        IsSystem      BIT           NOT NULL CONSTRAINT DF_LookupItems_System DEFAULT(0),
        CreatedDate   DATETIME2     NOT NULL CONSTRAINT DF_LookupItems_Created DEFAULT(SYSUTCDATETIME()),
        CreatedBy     NVARCHAR(256) NULL,
        ModifiedDate  DATETIME2     NULL,
        ModifiedBy    NVARCHAR(256) NULL
    );
    CREATE UNIQUE INDEX IX_LookupItems_Category_Code ON dbo.LookupItems(Category, Code);
    CREATE INDEX IX_LookupItems_Category ON dbo.LookupItems(Category);
END;

-- Idempotent seed: insert only rows whose (Category, Code) doesn't already exist.
;WITH seed(Category, Code, LabelEn, LabelAr, SortOrder) AS (
    SELECT * FROM (VALUES
        -- Property Type
        ('PropertyType','Apartment','Apartment',N'شقة',1),
        ('PropertyType','House','House',N'منزل',2),
        ('PropertyType','Commercial','Commercial',N'تجاري',3),
        ('PropertyType','Condo','Condo',N'شقة تمليك',4),
        -- Governorate
        ('Governorate','Al Asimah','Al Asimah',N'العاصمة',1),
        ('Governorate','Hawalli','Hawalli',N'حولي',2),
        ('Governorate','Farwaniya','Farwaniya',N'الفروانية',3),
        ('Governorate','Ahmadi','Ahmadi',N'الأحمدي',4),
        ('Governorate','Jahra','Jahra',N'الجهراء',5),
        ('Governorate','Mubarak Al Kabeer','Mubarak Al Kabeer',N'مبارك الكبير',6),
        -- Unit Type
        ('UnitType','Apartment','Apartment',N'شقة',1),
        ('UnitType','Studio','Studio',N'استوديو',2),
        ('UnitType','Office','Office',N'مكتب',3),
        ('UnitType','Shop','Shop',N'محل',4),
        ('UnitType','Commercial','Commercial',N'تجاري',5),
        ('UnitType','Penthouse','Penthouse',N'بنتهاوس',6),
        ('UnitType','Warehouse','Warehouse',N'مستودع',7),
        ('UnitType','Other','Other',N'أخرى',8),
        -- Unit Size Band
        ('UnitSizeBand','Studio','Studio',N'استوديو',1),
        ('UnitSizeBand','1BHK','1BHK',N'غرفة وصالة',2),
        ('UnitSizeBand','2BHK','2BHK',N'غرفتان وصالة',3),
        ('UnitSizeBand','3BHK+','3BHK+',N'ثلاث غرف فأكثر',4),
        ('UnitSizeBand','Commercial','Commercial',N'تجاري',5),
        -- Document Type
        ('DocumentType','Deed','Property Deed',N'سند ملكية',1),
        ('DocumentType','Title','Title Document',N'وثيقة ملكية',2),
        ('DocumentType','Building Permit','Building Permit',N'رخصة بناء',3),
        ('DocumentType','Occupancy Certificate','Occupancy Certificate',N'شهادة إشغال',4),
        ('DocumentType','Insurance Policy','Insurance Policy',N'بوليصة تأمين',5),
        ('DocumentType','Fire Safety Certificate','Fire Safety Certificate',N'شهادة سلامة من الحريق',6),
        ('DocumentType','Electrical Certificate','Electrical Certificate',N'شهادة كهرباء',7),
        ('DocumentType','Plumbing Certificate','Plumbing Certificate',N'شهادة سباكة',8),
        ('DocumentType','Municipality License','Municipality License',N'ترخيص بلدية',9),
        ('DocumentType','Property Survey','Property Survey',N'مسح عقاري',10),
        -- Maintenance Category
        ('MaintenanceCategory','Plumbing','Plumbing',N'سباكة',1),
        ('MaintenanceCategory','Electrical','Electrical',N'كهرباء',2),
        ('MaintenanceCategory','HVAC','HVAC',N'تكييف',3),
        ('MaintenanceCategory','Appliance','Appliance',N'أجهزة',4),
        ('MaintenanceCategory','General','General',N'عام',5),
        ('MaintenanceCategory','Other','Other',N'أخرى',6),
        -- Maintenance Priority
        ('MaintenancePriority','Low','Low',N'منخفضة',1),
        ('MaintenancePriority','Medium','Medium',N'متوسطة',2),
        ('MaintenancePriority','High','High',N'عالية',3),
        -- Payment Method
        ('PaymentMethod','KNET','KNET',N'كي-نت',1),
        ('PaymentMethod','Cash','Cash',N'نقدي',2),
        ('PaymentMethod','BankTransfer','Bank Transfer',N'تحويل بنكي',3),
        -- Legal Case Type
        ('LegalCaseType','Eviction','Eviction',N'إخلاء',1),
        ('LegalCaseType','NonPayment','Non-payment',N'عدم سداد',2),
        ('LegalCaseType','Damages','Damages',N'أضرار',3),
        ('LegalCaseType','Other','Other',N'أخرى',4),
        -- Insurance Frequency
        ('InsuranceFrequency','None','None',N'بدون',1),
        ('InsuranceFrequency','Monthly','Monthly',N'شهري',2),
        ('InsuranceFrequency','Annual','Annual',N'سنوي',3),
        ('InsuranceFrequency','OneTime','One-time',N'مرة واحدة',4)
    ) v(Category, Code, LabelEn, LabelAr, SortOrder)
)
INSERT INTO dbo.LookupItems (Category, Code, LabelEn, LabelAr, SortOrder, IsActive, IsSystem, CreatedDate, CreatedBy)
SELECT s.Category, s.Code, s.LabelEn, s.LabelAr, s.SortOrder, 1, 1, SYSUTCDATETIME(), 'seed'
FROM seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.LookupItems l WHERE l.Category = s.Category AND l.Code = s.Code);
