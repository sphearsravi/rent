import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Arabic (`ar`).
class AppLocalizationsAr extends AppLocalizations {
  AppLocalizationsAr([String locale = 'ar']) : super(locale);

  @override
  String get appName => 'ڤيجار للمستأجرين';

  @override
  String get loginTitle => 'تسجيل الدخول';

  @override
  String get loginCivilIdLabel => 'الرقم المدني';

  @override
  String get loginCivilIdHint => 'أدخل الرقم المدني';

  @override
  String get loginCivilIdRequired => 'يرجى إدخال الرقم المدني';

  @override
  String get loginCivilIdInvalid => 'يجب أن يتكون الرقم المدني من 12 رقمًا ويبدأ بـ 2 أو 3.';

  @override
  String get loginPasswordLabel => 'كلمة المرور';

  @override
  String get loginPasswordHint => 'أدخل كلمة المرور';

  @override
  String get loginPasswordRequired => 'يرجى إدخال كلمة المرور';

  @override
  String get loginRememberMe => 'تذكرني لمدة 30 يومًا';

  @override
  String get loginInvalidCredentials => 'الرقم المدني أو كلمة المرور غير صحيحة.';

  @override
  String get loginContactAdmin => 'إذا واجهت مشكلة في تسجيل الدخول، يرجى التواصل مع مدير العقار.';

  @override
  String get loginSubtitle => 'سجّل الدخول باستخدام الرقم المدني';

  @override
  String get loginTenantPortal => 'بوابة المستأجر';

  @override
  String get loginNeedHelp => 'تحتاج مساعدة؟ تواصل مع مدير العقار';

  @override
  String get logout => 'تسجيل الخروج';

  @override
  String get logoutConfirmTitle => 'تسجيل الخروج';

  @override
  String get logoutConfirmMessage => 'هل أنت متأكد من تسجيل الخروج؟';

  @override
  String get cancel => 'إلغاء';

  @override
  String get retry => 'إعادة المحاولة';

  @override
  String get submit => 'إرسال';

  @override
  String get close => 'إغلاق';

  @override
  String get done => 'تم';

  @override
  String get checkAgain => 'إعادة التحقق';

  @override
  String get refresh => 'تحديث';

  @override
  String get errorGeneric => 'حدث خطأ ما. حاول مرة أخرى.';

  @override
  String get errorNetwork => 'تعذر الاتصال. يرجى التحقق من الإنترنت.';

  @override
  String get errorTimeout => 'انتهت مهلة الطلب. حاول مرة أخرى.';

  @override
  String get errorSessionExpired => 'انتهت الجلسة. يرجى تسجيل الدخول مجددًا.';

  @override
  String get errorForbidden => 'ليس لديك صلاحية للقيام بهذا الإجراء.';

  @override
  String get errorNotFound => 'لم نتمكن من العثور على ما تبحث عنه.';

  @override
  String get errorServer => 'هناك مشكلة في الخادم. حاول لاحقًا.';

  @override
  String get paymentStatusPending => 'قيد الانتظار';

  @override
  String get paymentStatusCompleted => 'مكتملة';

  @override
  String get paymentStatusFailed => 'فشلت';

  @override
  String get paymentStatusCancelled => 'ملغاة';

  @override
  String get invoiceStatusPending => 'قيد الانتظار';

  @override
  String get invoiceStatusPartiallyPaid => 'مدفوعة جزئيًا';

  @override
  String get invoiceStatusPaid => 'مدفوعة';

  @override
  String get invoiceStatusOverdue => 'متأخرة';

  @override
  String get navDashboard => 'الرئيسية';

  @override
  String get navPayments => 'المدفوعات';

  @override
  String get navInvoices => 'الفواتير';

  @override
  String get navLeases => 'العقود';

  @override
  String get navMaintenance => 'الصيانة';

  @override
  String get navProfile => 'الملف الشخصي';

  @override
  String get greetingMorning => 'صباح الخير،';

  @override
  String get greetingAfternoon => 'مساء الخير،';

  @override
  String get greetingEvening => 'مساء الخير،';

  @override
  String get dashboardSubtitle => 'إليك لوحة الإيجار الخاصة بك';

  @override
  String get dashboardTenantFallback => 'مستأجر';

  @override
  String get statActiveLeases => 'العقود النشطة';

  @override
  String get statTotalPayments => 'إجمالي المدفوعات';

  @override
  String get statPendingRequests => 'الطلبات المعلّقة';

  @override
  String get dashboardMyProperties => 'عقاراتي';

  @override
  String get dashboardViewInvoices => 'عرض الفواتير';

  @override
  String get dashboardNoLeases => 'لا توجد عقود نشطة';

  @override
  String get dashboardRecentPayments => 'المدفوعات الأخيرة';

  @override
  String get dashboardNoPayments => 'لا توجد مدفوعات';

  @override
  String get dashboardMaintenanceTitle => 'طلبات الصيانة';

  @override
  String get dashboardNewRequest => 'طلب جديد';

  @override
  String get dashboardNoMaintenance => 'لا توجد طلبات صيانة معلّقة';

  @override
  String get invoicesTitle => 'الفواتير';

  @override
  String get invoicesFilterAll => 'الكل';

  @override
  String get invoicesFilterOutstanding => 'غير مدفوعة';

  @override
  String get invoicesFilterOverdue => 'متأخرة';

  @override
  String get invoicesFilterPaid => 'مدفوعة';

  @override
  String get invoicesEmptyTitle => 'لا توجد فواتير لعرضها';

  @override
  String get invoicesEmptyHint => 'اسحب للأسفل للتحديث';

  @override
  String get invoicesErrorTitle => 'خطأ في تحميل الفواتير';

  @override
  String get invoicesFailedToLoad => 'تعذّر تحميل الفواتير';

  @override
  String invoiceDetailTitle(int id) {
    return 'فاتورة رقم $id';
  }

  @override
  String invoiceUnit(String unit) {
    return 'وحدة $unit';
  }

  @override
  String get invoiceTypeLabel => 'النوع';

  @override
  String get invoiceTypeRent => 'إيجار';

  @override
  String get invoiceDateLabel => 'تاريخ الفاتورة';

  @override
  String get invoiceDueDateLabel => 'تاريخ الاستحقاق';

  @override
  String get invoiceTotalLabel => 'الإجمالي';

  @override
  String get invoicePaidLabel => 'المدفوع';

  @override
  String get invoiceBalanceLabel => 'المتبقي';

  @override
  String get invoicePaymentsLabel => 'المدفوعات';

  @override
  String get invoiceNoPayments => 'لا توجد مدفوعات مسجلة بعد.';

  @override
  String get invoiceReceipt => 'الإيصال';

  @override
  String get invoicePayNow => 'ادفع الآن';

  @override
  String get invoiceDownloadPdf => 'تنزيل PDF';

  @override
  String get invoiceNotFound => 'الفاتورة غير موجودة';

  @override
  String get invoiceFailedToLoad => 'تعذّر تحميل الفاتورة';

  @override
  String get paymentStatusScreenTitle => 'حالة الدفع';

  @override
  String get paymentChecking => 'جارٍ التحقق من حالة الدفع...';

  @override
  String get paymentSuccessful => 'تم الدفع بنجاح!';

  @override
  String get paymentResultFailed => 'فشل الدفع';

  @override
  String get paymentProcessing => 'جارٍ معالجة الدفع...';

  @override
  String get paymentStillProcessing => 'لا يزال الدفع قيد المعالجة. يمكنك التحقق مرة أخرى بعد قليل.';

  @override
  String get paymentUnableToVerify => 'تعذّر التحقق من حالة الدفع. راجع الفاتورة لمعرفة آخر حالة.';

  @override
  String get paymentUnableToVerifyShort => 'تعذّر التحقق من حالة الدفع';

  @override
  String get paymentInvalidReference => 'مرجع دفع غير صالح';

  @override
  String get paymentReferenceLabel => 'المرجع: ';

  @override
  String get maintenanceTitle => 'طلبات الصيانة';

  @override
  String get maintenanceEmptyTitle => 'لا توجد طلبات صيانة';

  @override
  String get maintenanceEmptyHint => 'أرسل طلبًا إذا كنت بحاجة إلى إصلاحات';

  @override
  String get maintenanceNewRequest => 'طلب جديد';

  @override
  String get maintenanceFailedToLoad => 'تعذّر تحميل الطلبات';

  @override
  String get maintenanceCreateTitle => 'طلب صيانة جديد';

  @override
  String get maintenancePropertyUnit => 'العقار/الوحدة';

  @override
  String get maintenanceSelectProperty => 'اختر عقارًا...';

  @override
  String get maintenanceSelectPropertyError => 'يرجى اختيار عقار';

  @override
  String get maintenanceCategory => 'الفئة';

  @override
  String get maintenanceSelectCategory => 'اختر فئة...';

  @override
  String get maintenanceSelectCategoryError => 'يرجى اختيار فئة';

  @override
  String get maintenancePriority => 'الأولوية';

  @override
  String get maintenanceSelectPriority => 'اختر الأولوية...';

  @override
  String get maintenanceSelectPriorityError => 'يرجى اختيار الأولوية';

  @override
  String get maintenanceDescription => 'الوصف';

  @override
  String get maintenanceDescriptionHint => 'يرجى وصف المشكلة بالتفصيل...';

  @override
  String get maintenanceDescriptionError => 'يرجى وصف المشكلة';

  @override
  String get maintenancePhotosOptional => 'الصور (اختياري)';

  @override
  String get maintenancePhotosHint => 'أضف حتى 5 صور. الكاميرا أو المعرض.';

  @override
  String get maintenanceCamera => 'الكاميرا';

  @override
  String get maintenanceGallery => 'المعرض';

  @override
  String get maintenanceSubmitRequest => 'إرسال الطلب';

  @override
  String get maintenanceSubmitting => 'جارٍ الإرسال...';

  @override
  String get maintenanceSubmitted => 'تم إرسال طلب الصيانة بنجاح!';

  @override
  String get profileTitle => 'ملفي الشخصي';

  @override
  String get profileFailedToLoad => 'تعذّر تحميل الملف الشخصي';

  @override
  String get language => 'اللغة';

  @override
  String get profileSectionPersonal => 'المعلومات الشخصية';

  @override
  String get profileLabelCivilId => 'الرقم المدني';

  @override
  String get profileLabelPhone => 'الهاتف';

  @override
  String get profileLabelEmail => 'البريد الإلكتروني';

  @override
  String get profileLabelDob => 'تاريخ الميلاد';

  @override
  String get profileLabelCompany => 'الشركة';

  @override
  String get profileLabelEmergencyContact => 'جهة الاتصال للطوارئ';

  @override
  String get profileLabelEmergencyPhone => 'هاتف الطوارئ';

  @override
  String get profileStatusActive => 'نشط';

  @override
  String get profileNotAvailable => 'غير متوفر';

  @override
  String get profileActionChangePassword => 'تغيير كلمة المرور';

  @override
  String get profileActionHelp => 'المساعدة والدعم';

  @override
  String get profileActionAbout => 'حول التطبيق';

  @override
  String get profileHelpToast => 'تواصل مع مدير العقار للمساعدة';

  @override
  String get changePasswordTitle => 'تغيير كلمة المرور';

  @override
  String get changePasswordCurrent => 'كلمة المرور الحالية';

  @override
  String get changePasswordNew => 'كلمة المرور الجديدة';

  @override
  String get changePasswordConfirm => 'تأكيد كلمة المرور الجديدة';

  @override
  String get changePasswordSubmit => 'تحديث كلمة المرور';

  @override
  String get changePasswordSuccess => 'تم تغيير كلمة المرور';

  @override
  String get changePasswordMismatch => 'كلمتا المرور غير متطابقتين';

  @override
  String get makePayment => 'إجراء دفعة';

  @override
  String get updateNow => 'التحديث الآن';

  @override
  String get submitTransfer => 'إرسال التحويل';

  @override
  String get backToDashboard => 'العودة إلى لوحة التحكم';

  @override
  String get changeAction => 'تغيير';

  @override
  String get changePasswordFailed => 'تعذّر تغيير كلمة المرور';

  @override
  String aboutVersion(String version) {
    return 'الإصدار $version';
  }

  @override
  String get aboutTagline => 'نظام VIIJAR لتحصيل الإيجار - تطبيق المستأجر';

  @override
  String get aboutDescription => 'أدِر عقود إيجارك، وادفع مستحقاتك، وقدّم طلبات الصيانة من مكان واحد.';

  @override
  String get paymentViewReceipt => 'عرض الإيصال';

  @override
  String get paymentSelectAndPay => 'اختيار ودفع';

  @override
  String get paymentPayNow => 'ادفع الآن';

  @override
  String get paymentKnetSubtitle => 'ادفع فورًا عبر كي نت';

  @override
  String get paymentBankTransfer => 'تحويل بنكي';

  @override
  String get paymentBankTransferSubtitle => 'ارفع إيصال التحويل';

  @override
  String get paymentUploadReceipt => 'رفع الإيصال';

  @override
  String get paymentAuthError => 'خطأ في المصادقة. يرجى تسجيل الدخول مرة أخرى.';

  @override
  String get paymentRedirectingKnet => 'جارٍ التحويل إلى بوابة الدفع كي نت...';

  @override
  String get paymentInitiateFailed => 'تعذّر بدء عملية الدفع';

  @override
  String get paymentSubmitBankTransferTitle => 'إرسال تحويل بنكي';

  @override
  String get paymentBankInstructionsTitle => 'تعليمات التحويل البنكي';

  @override
  String get paymentBankInstructionsBody => '1. حوّل المبلغ إلى حسابنا البنكي\n2. التقط صورة أو لقطة شاشة لإيصال التحويل\n3. املأ التفاصيل أدناه\n4. سيتحقق فريقنا من دفعتك';

  @override
  String get paymentAmountKwd => 'المبلغ (د.ك)';

  @override
  String get paymentTransferDate => 'تاريخ التحويل';

  @override
  String get paymentBankName => 'اسم البنك *';

  @override
  String get paymentSelectBankValidation => 'يرجى اختيار البنك';

  @override
  String get paymentReferenceOptional => 'الرقم المرجعي (اختياري)';

  @override
  String get paymentNotesOptional => 'ملاحظات (اختياري)';

  @override
  String get paymentTransferProof => 'إثبات التحويل *';

  @override
  String get paymentAttachProofFirst => 'يرجى إرفاق إثبات التحويل (صورة الإيصال) أولاً.';

  @override
  String get paymentBankTransferSuccess => 'تم إرسال التحويل البنكي بنجاح! في انتظار التحقق.';

  @override
  String get paymentSubmitTransferFailed => 'تعذّر إرسال التحويل';

  @override
  String get paymentSelectInvoice => 'اختر فاتورة للدفع';

  @override
  String get paymentChooseMethod => 'اختر طريقة الدفع';

  @override
  String get paymentInvoiceAmount => 'مبلغ الفاتورة:';

  @override
  String get paymentCash => 'الدفع نقدًا';

  @override
  String get paymentCashDescription => 'ادفع في مكتب الإدارة. اذكر الرقم المرجعي أدناه حتى نطبّق دفعتك النقدية على الفاتورة الصحيحة.';

  @override
  String get paymentCashOfficeHours => 'ساعات العمل: الأحد - الخميس، 9 صباحًا - 5 مساءً. يُصدر الإيصال بعد التحقق من الدفعة.';

  @override
  String get paymentNoPendingInvoices => 'لا توجد فواتير مستحقة';

  @override
  String get paymentNoPendingInvoicesBody => 'ليست لديك أي فواتير غير مدفوعة حاليًا.';

  @override
  String get paymentErrorLoadingInvoices => 'خطأ في تحميل الفواتير';

  @override
  String invoiceNumberShort(String id) {
    return 'فاتورة #$id';
  }

  @override
  String dueColonLabel(String date) {
    return 'الاستحقاق: $date';
  }

  @override
  String get labelProperty => 'العقار';

  @override
  String get labelUnit => 'الوحدة';

  @override
  String get notAvailable => 'غير متوفر';

  @override
  String get dashboardFailedToLoad => 'فشل تحميل البيانات';

  @override
  String get leasesFailedToLoad => 'فشل تحميل عقود الإيجار';

  @override
  String get leasesEmptyTitle => 'لا توجد عقارات';

  @override
  String get leasesEmptyHint => 'ليست لديك أي عقود إيجار سارية حاليًا';

  @override
  String get forceUpdateTitle => 'التحديث مطلوب';

  @override
  String get forceUpdateBody => 'لم يعد هذا الإصدار مدعومًا. يرجى التحديث إلى أحدث إصدار للمتابعة.';

  @override
  String get sessionRelogin => 'يرجى تسجيل الدخول مرة أخرى.';

  @override
  String invoiceDownloadFailed(String code) {
    return 'تعذّر تنزيل الفاتورة (HTTP $code).';
  }

  @override
  String receiptDownloadFailed(String code) {
    return 'تعذّر تنزيل الإيصال (HTTP $code).';
  }

  @override
  String pdfOpenFailed(String message) {
    return 'تعذّر فتح ملف PDF: $message';
  }

  @override
  String invoicePdfError(String error) {
    return 'خطأ في فاتورة PDF: $error';
  }

  @override
  String receiptError(String error) {
    return 'خطأ في الإيصال: $error';
  }

  @override
  String get labelCategory => 'الفئة';

  @override
  String get labelPriority => 'الأولوية';

  @override
  String get labelRequestDate => 'تاريخ الطلب';

  @override
  String get labelCompletedDate => 'تاريخ الإنجاز';

  @override
  String get labelAssignedTo => 'مُسند إلى';

  @override
  String get labelDescription => 'الوصف';

  @override
  String get labelNotes => 'ملاحظات';

  @override
  String requestNumberShort(String id) {
    return 'طلب #$id';
  }

  @override
  String requestedOnLabel(String date) {
    return 'تم الطلب في $date';
  }

  @override
  String get labelUnitNumber => 'رقم الوحدة';

  @override
  String get labelMonthlyRent => 'الإيجار الشهري';

  @override
  String get labelSecurityDeposit => 'مبلغ التأمين';

  @override
  String get labelStartDate => 'تاريخ البداية';

  @override
  String get labelEndDate => 'تاريخ النهاية';

  @override
  String get labelDaysRemaining => 'الأيام المتبقية';

  @override
  String get labelPaymentDueDay => 'يوم استحقاق الدفع';

  @override
  String get statusExpired => 'منتهٍ';

  @override
  String get statusExpiringSoon => 'ينتهي قريبًا';

  @override
  String daysValue(String days) {
    return '$days يوم';
  }

  @override
  String paymentDueDayValue(String day) {
    return 'اليوم $day من كل شهر';
  }

  @override
  String get statusPending => 'قيد الانتظار';

  @override
  String get statusPaid => 'مدفوعة';

  @override
  String get statusPartiallyPaid => 'مدفوعة جزئيًا';

  @override
  String get statusOverdue => 'متأخرة';

  @override
  String get statusCancelled => 'ملغاة';

  @override
  String get statusCompleted => 'مكتملة';

  @override
  String get statusFailed => 'فاشلة';

  @override
  String get statusRefunded => 'مُستردة';

  @override
  String get statusActive => 'نشط';

  @override
  String get statusTerminated => 'منهاة';

  @override
  String get statusInProgress => 'قيد التنفيذ';

  @override
  String get statusApproved => 'موافق عليها';

  @override
  String get statusRejected => 'مرفوضة';

  @override
  String get statusOpen => 'مفتوحة';

  @override
  String get statusAssigned => 'مُسندة';

  @override
  String get payInvoicesInOrder => 'ادفع الفواتير بالترتيب. ';

  @override
  String startWithInvoiceHint(String id, String count) {
    return 'ابدأ بالفاتورة #$id. ستُفتح الفواتير المتبقية ($count) بعد سدادها بالكامل.';
  }

  @override
  String payInvoiceFirst(String id) {
    return 'ادفع الفاتورة #$id أولاً';
  }
}
