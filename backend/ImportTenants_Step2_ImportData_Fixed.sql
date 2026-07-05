-- ========================================
-- Rent Collection System
-- Tenant Import - Step 2: Import Tenant Data
-- Matches tenants to properties using PACI numbers
-- ========================================

USE RentCollectionDB;
GO

PRINT '========================================';
PRINT 'Starting Tenant Import Process';
PRINT '========================================';
PRINT '';

-- Get the default CompanyId
DECLARE 1 INT;
SELECT TOP 1 1 = CompanyId FROM Companies ORDER BY CompanyId;

PRINT 'Using CompanyId: ' + CAST(1 AS VARCHAR);
PRINT '';

-- ========================================
-- STEP 1: Import all tenant records
-- ========================================

PRINT 'Step 1: Importing tenant records...';

INSERT INTO Tenants (CompanyId, FirstName, LastName, Email, Phone, DateOfBirth, GovernmentId, EmploymentStatus, EmergencyContact, EmergencyPhone, Status, CreatedDate, ModifiedDate, PACINumber)
SELECT 1, FirstName, LastName, Email, Phone, DateOfBirth, GovernmentId, EmploymentStatus, EmergencyContact, EmergencyPhone, Status, CreatedDate, ModifiedDate, PACINumber
FROM (VALUES
-- Building 123 (PACI: 15815085) - 20 tenants
(1, N'شركة', N'االهرفي البوادي لبيع الأغذية والمشروبات بالجملة', 'unknown@tenant.com', '99320008', NULL, '270122000631', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'مشاعل', N'احمد حسن حجي', 'unknown@tenant.com', '67005577', NULL, '278072000022', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'عياد', N'فراج مناحي العنزي', 'unknown@tenant.com', '99640009', NULL, '263012000942', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'رامي', N'عوض محمود الطويله', 'unknown@tenant.com', '66901845', NULL, '284052308661', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'لبيب', N'عزت العيد', 'unknown@tenant.com', '69648178', NULL, '271122801126', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'محمد', N'غريب عبدالعاطي', 'unknown@tenant.com', '99036602', NULL, '284022805253', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'محمد', N'جبر محمد محمد', 'unknown@tenant.com', '99874397', NULL, '285020907525', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'فواز', N'حمدان مفلح الصليلي', 'unknown@tenant.com', '55795284', NULL, '284110400854', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'أحمد', N'السعيد الشاعر', 'unknown@tenant.com', '65998187', NULL, '285041409904', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'احمد', N'ممدوح الحلقي', 'unknown@tenant.com', '98884410', NULL, '267121601569', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'محمد', N'صلاح السيد', 'unknown@tenant.com', '99083098', NULL, '276080105247', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'ابراهيم', N'السيد شلبي', 'unknown@tenant.com', '98551750', NULL, '285112306209', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'سعد', N'خلف عبدالله', 'unknown@tenant.com', '99993056', NULL, '272030101151', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'طارق', N'حمود الحسين', 'unknown@tenant.com', '55399713', NULL, '281112701227', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'موفق', N'مفلح الحارون', 'unknown@tenant.com', '99883554', NULL, '263052001042', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'خالد', N'ارمضان الحمدان', 'unknown@tenant.com', '50074733', NULL, '260081503283', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),
(1, N'عبيد', N'فلاح الضويحي', 'unknown@tenant.com', '65888040', NULL, '269012100609', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15815085'),

-- Building 131 (PACI: 15817486) - 2 tenants
(1, N'سعاد', N'شلاش عبد صفوق', 'unknown@tenant.com', '66701545', NULL, '2591024000315', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817486'),
(1, N'عصام', N'فرحان عبد ضامن العنزي', 'unknown@tenant.com', '94449944', NULL, '276091200696', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817486'),

-- Building 132 (PACI: 15817785) - 18 tenants
(1, N'حمد', N'هايف محمد العتيبي', 'unknown@tenant.com', '99860005', NULL, '266112800609', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'خالد', N'عبدالله محمد الحبشي', 'unknown@tenant.com', '66043969', NULL, '247050301017', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'بسام', N'حسنى خليف', 'unknown@tenant.com', '51271256', NULL, '278070308015', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'شادي', N'محمد صلاح الدين', 'unknown@tenant.com', '66322131', NULL, '284010608105', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'ابراهيم', N'محمود عبدالقادر', 'unknown@tenant.com', '67703738', NULL, '289010107629', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'ابراهيم', N'احمد الابراهيم', 'unknown@tenant.com', '55512508', NULL, '291040401549', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'صالح', N'حماده النايف', 'unknown@tenant.com', '96660303', NULL, '281012001629', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'وائل', N'بشير المحمد', 'unknown@tenant.com', '50025726', NULL, '288100102659', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'اكرم', N'ممدوح الحجار', 'unknown@tenant.com', '94038931', NULL, '281020800113', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'محمد', N'عدنان الزعبي', 'unknown@tenant.com', '60465724', NULL, '285030116233', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'مريم', N'محسن العبد المحمد', 'unknown@tenant.com', '97679295', NULL, '287050402068', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'خلدون', N'محمد حمد', 'unknown@tenant.com', '55729966', NULL, '283110301843', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'خليل', N'حميد الحميد', 'unknown@tenant.com', '98989192', NULL, '266040405254', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'عصام', N'محمد ابراهيم دياب', 'unknown@tenant.com', '66248821', NULL, '265061302727', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'مصطفى', N'عبدالحميد عبدالحليم', 'unknown@tenant.com', '99910953', NULL, '275071017603', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'بلال', N'عبد الحكيم عبدالقادر', 'unknown@tenant.com', '97866641', NULL, '283111504941', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'محمد', N'طه علي محمد', 'unknown@tenant.com', '55881914', NULL, '280112104956', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),
(1, N'عبيد', N'فلاح سعود الضويحي', 'unknown@tenant.com', '65888040', NULL, '269012100609', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15817785'),

-- Building 133 (PACI: 15818059) - 23 tenants
(1, N'عبدالله', N'محمد فهيد العجمي', 'unknown@tenant.com', '55333680', NULL, '273070200781', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'عياد', N'فراج مناحى العنزي', 'unknown@tenant.com', '99640009', NULL, '263012000942', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'عبدو', N'خربوطلي', 'unknown@tenant.com', '97191595', NULL, '263010127595', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'مكتب', N'', 'maktab@tenant.com', NULL, NULL, '', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'ابراهيم', N'نادر الحلقى', 'unknown@tenant.com', '97637445', NULL, '281021004047', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'محمد', N'عبدالرحمن ابراهيم', 'unknown@tenant.com', '60659751', NULL, '269020501074', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'أيمن', N'امجد محمد الخولي', 'unknown@tenant.com', '60678391', NULL, '285032705146', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'محمد', N'فايز أمين محمد', 'unknown@tenant.com', '97636623', NULL, '275110204699', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'عثمان', N'بسيوني عثمان', 'unknown@tenant.com', '97636623', NULL, '273010159211', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'عمر', N'سيد احمد العمراوي', 'unknown@tenant.com', '67617574', NULL, '272122005917', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'يوسف', N'كوينو الهارون', 'unknown@tenant.com', '56615277', NULL, '296100100426', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'محمد', N'عبدالعزيز محمد', 'unknown@tenant.com', '55866015', NULL, '274020507309', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'ابتسام', N'محمد عبدالوهاب', 'unknown@tenant.com', '66706981', NULL, '264052804704', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'زياد', N'صبرى قداح', 'unknown@tenant.com', '50576466', NULL, '284080804996', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'امل', N'عبدالوهاب عبدالعليم', 'unknown@tenant.com', '99478489', NULL, '277010131479', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'محمد', N'عبدالموجود اسماعيل', 'unknown@tenant.com', '65780928', NULL, '273020504365', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'هشام', N'ابراهيم محمود العطفي', 'unknown@tenant.com', '65723010', NULL, '282100109538', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'نسرين', N'محمد احمد يوسف', 'unknown@tenant.com', '65839799', NULL, '270040408107', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'علاء', N'الدين كمال ابوالعلا', 'unknown@tenant.com', '94015856', NULL, '276123105651', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'سامح', N'احمد مصطفى', 'unknown@tenant.com', '55533106', NULL, '272072203666', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'فؤاد', N'بوعياد', 'unknown@tenant.com', '96664928', NULL, '273040506843', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),
(1, N'شريف', N'عزت السيد خورشيد', 'unknown@tenant.com', '66794241', NULL, '287092017133', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '15818059'),

-- قسيمة 201 (PACI: 95049746) - 3 tenants
(1, N'راشد', N'محمد العجمي', 'unknown@tenant.com', NULL, NULL, '279111201287', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95049746'),
(1, N'حجرف', N'مبارك الحجرف', 'unknown@tenant.com', '50666111', NULL, '285091700863', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95049746'),
(1, N'نواف', N'سعد ربيح العنـــــزي', 'unknown@tenant.com', NULL, NULL, '287111400198', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95049746'),

-- قسيمة 219 (PACI: 95645126) - 12 tenants
(1, N'فارس', N'برغش برغش المطيري', 'unknown@tenant.com', '55200008', NULL, '277122900869', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'فيصل', N'عبدالرازق', 'unknown@tenant.com', '97773334', NULL, '282070700844', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'علي', N'بشير الأسود', 'unknown@tenant.com', '55979560', NULL, '258020503914', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'منذر', N'زياد الطحان', 'unknown@tenant.com', '55293131', NULL, '291071501059', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'محي', N'الدين موسى', 'unknown@tenant.com', '96627534', NULL, '272092901314', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'حمود', N'فهد خالد', 'unknown@tenant.com', '96022780', NULL, '276111301217', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'بدر', N'جفران محسن الحسيني', 'unknown@tenant.com', '55485953', NULL, '273122800843', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'هدى', N'دغش فلاح الحجرف', 'unknown@tenant.com', '52225890', NULL, '273062101169', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'ابراهيم', N'موسى قطاش', 'unknown@tenant.com', '96627533', NULL, '271110101624', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'نواف', N'عتيق محمد السالم', 'unknown@tenant.com', '55770055', NULL, '272101500601', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),
(1, N'عارف', N'محمود سعيد', 'unknown@tenant.com', '50296557', NULL, '269020207502', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645126'),

-- عمارة 220 (PACI: 95645134) - 12 tenants
(1, N'غازي', N'عبيد فجري الحسيني', 'unknown@tenant.com', '66090062', NULL, '274112401288', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'سعد', N'عامر شمال الشمري', 'unknown@tenant.com', '94114113', NULL, '265010500774', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'سلطان', N'ضحوي علوي الظفيري', 'unknown@tenant.com', '97111750', NULL, '272101100536', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'محمد', N'صالح الحميدي القحص العنزي', 'unknown@tenant.com', '50647417', NULL, '285102900677', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'طلال', N'فرحان شاهر', 'unknown@tenant.com', '55784554', NULL, '', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'محمد', N'حبيب مححمد الظفيري', 'unknown@tenant.com', '51577751', NULL, '270102000589', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'عبدالرحمن', N'ثويني العنزي', 'unknown@tenant.com', '66626399', NULL, '284051201141', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'عبدالله', N'يوعد عبدالله الحربي', 'unknown@tenant.com', '90055680', NULL, '291052300518', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'حسنية', N'علي  هدروس', 'unknown@tenant.com', '99434198', NULL, '270021701084', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'عبدالحكيم', N'عبدالرحمن عبدالقادر', 'unknown@tenant.com', '96618175', NULL, '267091701269', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'علي', N'عوض مصارع', 'unknown@tenant.com', '99519099', NULL, '270081900472', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),
(1, N'ماطر', N'سعيدان مطر السليماني', 'unknown@tenant.com', '99307096', NULL, '278122800838', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645134'),

-- عمارة 221 (PACI: 95645142) - 12 tenants
(1, N'محمد', N'دعسان دحيلان السليماني', 'unknown@tenant.com', '55229523', NULL, '286070300136', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'محمد', N'عبيد فليج الحسيني', 'unknown@tenant.com', '96667556', NULL, '280052500735', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'صلاح', N'جاسر سليمان سالم', 'unknown@tenant.com', '51115516', NULL, '286120300151', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'سارة', N'عبدالله غيطان مطلق', 'unknown@tenant.com', '50624447', NULL, '272060300514', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'خالد', N'فرحان خالد الشمري', 'unknown@tenant.com', '52525797', NULL, '293072301365', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'راشد', N'ركاد فراج العنزي', 'unknown@tenant.com', '51115154', NULL, '286071300023', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'احمد', N'رجاء عايد الشمري', 'unknown@tenant.com', '51022101', NULL, '270030801028', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'ابراهيم', N'عوض محياء الرشيدي', 'unknown@tenant.com', '60660096', NULL, '296041200047', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'ماهر', N'عثمان نعسان علو', 'unknown@tenant.com', '55949483', NULL, '287112300746', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'بشار', N'احمد خضر حميد', 'unknown@tenant.com', '94121511', NULL, '299082200979', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'بدر', N'مدلول وسمي الظفيري', 'unknown@tenant.com', '60693339', NULL, '278122301588', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),
(1, N'طاهر', N'سعيدان مطر السليماني', 'unknown@tenant.com', '66613353', NULL, '285100601075', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '95645142'),

-- ابوحليفة (PACI: 20500942) - 14 tenants
(1, N'الكويتية', N'البحرينية للصيرفة', 'unknown@tenant.com', '51280022', NULL, '280102100247', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'وربة', N'يونيفيرسال للتجارة', 'unknown@tenant.com', '55413681', NULL, '262111200873', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'بجده', N'عيد هويدي', 'unknown@tenant.com', '55353793', NULL, '244092300449', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'نبراس', N'المتميزه للتجاره', 'unknown@tenant.com', '99592184', NULL, '278060612339', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'مؤسسة', N'شامل ستور لأجهزة الهواتف النقالة', 'unknown@tenant.com', '90988404', NULL, '260071800727', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'نجلاء', N'جمل دليك العتيبي', 'unknown@tenant.com', '50624844', NULL, '274072101706', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'وضحه', N'جلوي سبعان العجمي', 'unknown@tenant.com', '67634787', NULL, '281011601315', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'محمد', N'حسن انور علي بدر', 'unknown@tenant.com', '98914480', NULL, '277092009214', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'عايض', N'سيف عايض العتيبي', 'unknown@tenant.com', '67634787', NULL, '275032201109', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),
(1, N'ابهيشيك', N'شاودهري', 'unknown@tenant.com', '66118271', NULL, '292012304631', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20500942'),

-- السالمية (PACI: 90252067) - 56 tenants
(1, N'جلال', N'احمد حميد الصفار', 'unknown@tenant.com', '99070666', NULL, '274050100585', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'اورينتال الدولية للتجارة العامة والمقاولات', 'unknown@tenant.com', '66975253', NULL, '259051502046', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'عدنان', N'عبدالله داود العبدالله', 'unknown@tenant.com', '99533119', NULL, '265112800356', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'كافوري للتجارة العامة', 'unknown@tenant.com', '60001692', NULL, '267081600237', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'اروى المشتركة للتجارة العامة والمقاولات', 'unknown@tenant.com', '66062866', NULL, '271012300355', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شويطر', N'سعدي عبيد', 'unknown@tenant.com', '90905747', NULL, '261051401029', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'بسام', N'موسى عباس', 'unknown@tenant.com', '99063038', NULL, '258103000466', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'أي كي المتحده للتجارة العامة', 'unknown@tenant.com', '66740352', NULL, '252101800075', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'يونيفرسال العالمية للتجارة العامة', 'unknown@tenant.com', '69906064', NULL, '272011507156', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'التكنولوجيا العالية', 'unknown@tenant.com', '97219995', NULL, '257033000158', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'البريطاني دليفري للتوصيل', 'unknown@tenant.com', '51156658', NULL, '286062001072', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'منير', N'صبحي مصطفى', 'unknown@tenant.com', '66660117', NULL, '266040100258', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'روما الدولية', 'unknown@tenant.com', '98824400', NULL, '285102201278', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'دار الحصن', 'unknown@tenant.com', '55044989', NULL, '285070102194', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'محمد', N'ايمن محمود', 'unknown@tenant.com', '99343132', NULL, '256021601912', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'سعاد', N'محمد حسين', 'unknown@tenant.com', '97993753', NULL, '268093000946', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'انور', N'حسين عبدالمزيد', 'unknown@tenant.com', '99845632', NULL, '276020108614', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'النجم الساحلي التونسي', 'unknown@tenant.com', '97994284', NULL, '285102300663', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'زيد', N'كنعان حسين', 'unknown@tenant.com', '90966604', NULL, '271090500213', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'محمد', N'اسماعيل السيد', 'unknown@tenant.com', NULL, NULL, '281051000485', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'محمد', N'غانم محمد الفيلكاوي', 'unknown@tenant.com', '51412177', NULL, '265090600723', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'لواحظ', N'علي موسى', 'unknown@tenant.com', '66409476', NULL, '265004300044', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'حسين', N'علي حسن محمد', 'unknown@tenant.com', '66415584', NULL, '262032900636', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'جورج', N'نبيل رمزي', 'unknown@tenant.com', '98929290', NULL, '291080501296', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'بيوني ليفت للتجارة العامة', 'unknown@tenant.com', '69060361', NULL, '270101000501', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'أنور', N'حمود علي العثمان', 'unknown@tenant.com', '66773140', NULL, '275111301319', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'الفنار الاهلية للتجارة', 'unknown@tenant.com', '60998889', NULL, '260020500847', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'مؤسسة', N'عقار للتجارة العامة والمقاولات', 'unknown@tenant.com', '97993753', NULL, '289090600033', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'جولدن توب للمقاولات', 'unknown@tenant.com', '60019818', NULL, '275100400872', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'الشركة', N'الرواج العالمية للتجارة', 'unknown@tenant.com', '67036068', NULL, '265030500026', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'النخبة الدولية لتجارة الجملة والتجزئة', 'unknown@tenant.com', '97696196', NULL, '266102402068', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'عيسى', N'عبدالله محمد', 'unknown@tenant.com', '55975250', NULL, '267081600237', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'يوسف', N'مبارك صالح', 'unknown@tenant.com', '55551346', NULL, '253070701115', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'لولوه', N'صالح رشم الظفيري', 'unknown@tenant.com', '99011916', NULL, '261093000442', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'جاسم', N'محمد عيسى', 'unknown@tenant.com', '66220055', NULL, '265122000416', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),
(1, N'شركة', N'كلين لتنظيف السجاد', 'unknown@tenant.com', '56666575', NULL, '289081701051', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '90252067'),

-- المنقف (PACI: 20000032, 20000059, 20000067, 20000024) - 8 tenants
(1, N'نورة', N'عايض عبدالله المطيري', 'unknown@tenant.com', '55526861', NULL, '270122000893', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000032'),
(1, N'رفعان', N'رصد عبدالهادي العجمي', 'unknown@tenant.com', '66321233', NULL, '285050501235', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000059'),
(1, N'عبدالله', N'عايش عماش العازمي', 'unknown@tenant.com', '55419009', NULL, '298052900099', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000067'),
(1, N'مشعل', N'عبيد سلوم محمد الشمري', 'unknown@tenant.com', '60925246', NULL, '270010800677', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000024'),
(1, N'بوبي', N'ميشيل فيلاتوتهارا اوسيف', 'unknown@tenant.com', '51179994', NULL, '286053013573', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000032'),
(1, N'بوبي', N'ميشيل فيلاتوتهارا اوسيف', 'unknown@tenant.com', '51179994', NULL, '286053013573', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000059'),
(1, N'ارشد', N'كوربيلاث موسى  نالوبور اباتيل', 'unknown@tenant.com', '55419009', NULL, '284010802135', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000067'),
(1, N'سانجاي', N'كومار بالاك ران', 'unknown@tenant.com', '66095852', NULL, '280021705201', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20000067'),

-- الصناعيات (PACI: 19666996, 19667016, 20023947, 96231734) - 3 tenants
(1, N'شركة', N'الكرم العربي للتجهيزات الغذائية', 'unknown@tenant.com', NULL, NULL, '265102609742', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '19666996'),
(1, N'شركة', N'الموفاس للتجارة العامة', 'unknown@tenant.com', '65064447', NULL, '270090900845', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '19667016'),
(1, N'شركة', N'كويت هايجين كونسبت', 'unknown@tenant.com', '60459581', NULL, '26611104786', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '20023947'),

-- الشماليات (PACI: 91227306, 91227314) - 2 tenants
(1, N'شركة', N'مطاحن ومحامص بن العميد', 'unknown@tenant.com', NULL, NULL, '242062300414', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '91227306'),
(1, N'محمود', N'أحمد المقداد', 'unknown@tenant.com', '66000021', NULL, '267041200976', NULL, NULL, NULL, 'Active', GETDATE(), GETDATE(), '91227314');

PRINT '  ✓ Successfully imported ' + CAST(@@ROWCOUNT AS VARCHAR) + ' tenant records';
PRINT '';

-- ========================================
-- STEP 2: Verify tenant-property matching
-- ========================================

PRINT 'Step 2: Verifying tenant-property matching...';
PRINT '';

SELECT
    p.PropertyName,
    p.PACINumber AS PropertyPACI,
    p.Governorate,
    p.Area,
    p.Block,
    COUNT(t.TenantId) AS TenantCount
FROM Properties p
LEFT JOIN Tenants t ON p.PACINumber = t.PACINumber AND t.PACINumber IS NOT NULL AND t.PACINumber != ''
GROUP BY p.PropertyId, p.PropertyName, p.PACINumber, p.Governorate, p.Area, p.Block
ORDER BY p.PropertyName;

PRINT '';
PRINT '========================================';
PRINT 'Tenant Import Complete!';
PRINT '';
PRINT 'Summary:';
PRINT '- Total tenants imported: ' + CAST((SELECT COUNT(*) FROM Tenants) AS VARCHAR);
PRINT '- Tenants with PACI numbers: ' + CAST((SELECT COUNT(*) FROM Tenants WHERE PACINumber IS NOT NULL AND PACINumber != '') AS VARCHAR);
PRINT '- Tenants matched to properties: ' + CAST((SELECT COUNT(DISTINCT t.TenantId) FROM Tenants t INNER JOIN Properties p ON t.PACINumber = p.PACINumber WHERE t.PACINumber IS NOT NULL AND t.PACINumber != '') AS VARCHAR);
PRINT '========================================';
GO
