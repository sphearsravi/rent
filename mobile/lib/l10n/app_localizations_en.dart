import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appName => 'VIIJAR Tenant';

  @override
  String get loginTitle => 'Sign In';

  @override
  String get loginCivilIdLabel => 'Civil ID';

  @override
  String get loginCivilIdHint => 'Enter your Civil ID';

  @override
  String get loginCivilIdRequired => 'Please enter your Civil ID';

  @override
  String get loginCivilIdInvalid => 'Civil ID must be 12 digits starting with 2 or 3.';

  @override
  String get loginPasswordLabel => 'Password';

  @override
  String get loginPasswordHint => 'Enter your password';

  @override
  String get loginPasswordRequired => 'Please enter your password';

  @override
  String get loginRememberMe => 'Remember me for 30 days';

  @override
  String get loginInvalidCredentials => 'Invalid Civil ID or password.';

  @override
  String get loginContactAdmin => 'If you have trouble signing in, contact your property manager.';

  @override
  String get loginSubtitle => 'Sign in with your Civil ID';

  @override
  String get loginTenantPortal => 'Tenant Portal';

  @override
  String get loginNeedHelp => 'Need help? Contact your property manager';

  @override
  String get logout => 'Logout';

  @override
  String get logoutConfirmTitle => 'Logout';

  @override
  String get logoutConfirmMessage => 'Are you sure you want to log out?';

  @override
  String get cancel => 'Cancel';

  @override
  String get retry => 'Retry';

  @override
  String get submit => 'Submit';

  @override
  String get close => 'Close';

  @override
  String get done => 'Done';

  @override
  String get checkAgain => 'Check Again';

  @override
  String get refresh => 'Refresh';

  @override
  String get errorGeneric => 'Something went wrong. Please try again.';

  @override
  String get errorNetwork => 'Unable to connect. Check your internet connection.';

  @override
  String get errorTimeout => 'The request timed out. Please try again.';

  @override
  String get errorSessionExpired => 'Session expired. Please log in again.';

  @override
  String get errorForbidden => 'You don\'t have permission to do this.';

  @override
  String get errorNotFound => 'We couldn\'t find what you were looking for.';

  @override
  String get errorServer => 'Server is having trouble. Please try again later.';

  @override
  String get paymentStatusPending => 'Pending';

  @override
  String get paymentStatusCompleted => 'Completed';

  @override
  String get paymentStatusFailed => 'Failed';

  @override
  String get paymentStatusCancelled => 'Cancelled';

  @override
  String get invoiceStatusPending => 'Pending';

  @override
  String get invoiceStatusPartiallyPaid => 'Partially Paid';

  @override
  String get invoiceStatusPaid => 'Paid';

  @override
  String get invoiceStatusOverdue => 'Overdue';

  @override
  String get navDashboard => 'Dashboard';

  @override
  String get navPayments => 'Payments';

  @override
  String get navInvoices => 'Invoices';

  @override
  String get navLeases => 'Leases';

  @override
  String get navMaintenance => 'Maintenance';

  @override
  String get navProfile => 'Profile';

  @override
  String get greetingMorning => 'Good morning,';

  @override
  String get greetingAfternoon => 'Good afternoon,';

  @override
  String get greetingEvening => 'Good evening,';

  @override
  String get dashboardSubtitle => 'Here\'s your rental dashboard';

  @override
  String get dashboardTenantFallback => 'Tenant';

  @override
  String get statActiveLeases => 'Active Leases';

  @override
  String get statTotalPayments => 'Total Payments';

  @override
  String get statPendingRequests => 'Pending Requests';

  @override
  String get dashboardMyProperties => 'My Properties';

  @override
  String get dashboardViewInvoices => 'View Invoices';

  @override
  String get dashboardNoLeases => 'No active leases found';

  @override
  String get dashboardRecentPayments => 'Recent Payments';

  @override
  String get dashboardNoPayments => 'No payments found';

  @override
  String get dashboardMaintenanceTitle => 'Maintenance Requests';

  @override
  String get dashboardNewRequest => 'New Request';

  @override
  String get dashboardNoMaintenance => 'No pending maintenance requests';

  @override
  String get invoicesTitle => 'Invoices';

  @override
  String get invoicesFilterAll => 'All';

  @override
  String get invoicesFilterOutstanding => 'Outstanding';

  @override
  String get invoicesFilterOverdue => 'Overdue';

  @override
  String get invoicesFilterPaid => 'Paid';

  @override
  String get invoicesEmptyTitle => 'No invoices to show';

  @override
  String get invoicesEmptyHint => 'Pull down to refresh';

  @override
  String get invoicesErrorTitle => 'Error loading invoices';

  @override
  String get invoicesFailedToLoad => 'Failed to load invoices';

  @override
  String invoiceDetailTitle(int id) {
    return 'Invoice #$id';
  }

  @override
  String invoiceUnit(String unit) {
    return 'Unit $unit';
  }

  @override
  String get invoiceTypeLabel => 'Type';

  @override
  String get invoiceTypeRent => 'Rent';

  @override
  String get invoiceDateLabel => 'Invoice date';

  @override
  String get invoiceDueDateLabel => 'Due date';

  @override
  String get invoiceTotalLabel => 'Total';

  @override
  String get invoicePaidLabel => 'Paid';

  @override
  String get invoiceBalanceLabel => 'Balance';

  @override
  String get invoicePaymentsLabel => 'Payments';

  @override
  String get invoiceNoPayments => 'No payments recorded yet.';

  @override
  String get invoiceReceipt => 'Receipt';

  @override
  String get invoicePayNow => 'Pay now';

  @override
  String get invoiceDownloadPdf => 'Download PDF';

  @override
  String get invoiceNotFound => 'Invoice not found';

  @override
  String get invoiceFailedToLoad => 'Failed to load invoice';

  @override
  String get paymentStatusScreenTitle => 'Payment Status';

  @override
  String get paymentChecking => 'Checking payment status...';

  @override
  String get paymentSuccessful => 'Payment Successful!';

  @override
  String get paymentResultFailed => 'Payment Failed';

  @override
  String get paymentProcessing => 'Payment is being processed...';

  @override
  String get paymentStillProcessing => 'Payment is still being processed. You can check again in a moment.';

  @override
  String get paymentUnableToVerify => 'Unable to verify payment status. Check the invoice for the latest status.';

  @override
  String get paymentUnableToVerifyShort => 'Unable to verify payment status';

  @override
  String get paymentInvalidReference => 'Invalid payment reference';

  @override
  String get paymentReferenceLabel => 'Reference: ';

  @override
  String get maintenanceTitle => 'Maintenance Requests';

  @override
  String get maintenanceEmptyTitle => 'No maintenance requests';

  @override
  String get maintenanceEmptyHint => 'Submit a request if you need repairs';

  @override
  String get maintenanceNewRequest => 'New Request';

  @override
  String get maintenanceFailedToLoad => 'Failed to load requests';

  @override
  String get maintenanceCreateTitle => 'New Maintenance Request';

  @override
  String get maintenancePropertyUnit => 'Property/Unit';

  @override
  String get maintenanceSelectProperty => 'Select a property...';

  @override
  String get maintenanceSelectPropertyError => 'Please select a property';

  @override
  String get maintenanceCategory => 'Category';

  @override
  String get maintenanceSelectCategory => 'Select a category...';

  @override
  String get maintenanceSelectCategoryError => 'Please select a category';

  @override
  String get maintenancePriority => 'Priority';

  @override
  String get maintenanceSelectPriority => 'Select priority...';

  @override
  String get maintenanceSelectPriorityError => 'Please select a priority';

  @override
  String get maintenanceDescription => 'Description';

  @override
  String get maintenanceDescriptionHint => 'Please describe the issue in detail...';

  @override
  String get maintenanceDescriptionError => 'Please describe the issue';

  @override
  String get maintenancePhotosOptional => 'Photos (optional)';

  @override
  String get maintenancePhotosHint => 'Add up to 5 photos. Camera or gallery.';

  @override
  String get maintenanceCamera => 'Camera';

  @override
  String get maintenanceGallery => 'Gallery';

  @override
  String get maintenanceSubmitRequest => 'Submit Request';

  @override
  String get maintenanceSubmitting => 'Submitting...';

  @override
  String get maintenanceSubmitted => 'Maintenance request submitted successfully!';

  @override
  String get profileTitle => 'My Profile';

  @override
  String get profileFailedToLoad => 'Failed to load profile';

  @override
  String get language => 'Language';

  @override
  String get profileSectionPersonal => 'Personal Information';

  @override
  String get profileLabelCivilId => 'Civil ID';

  @override
  String get profileLabelPhone => 'Phone';

  @override
  String get profileLabelEmail => 'Email';

  @override
  String get profileLabelDob => 'Date of Birth';

  @override
  String get profileLabelCompany => 'Company';

  @override
  String get profileLabelEmergencyContact => 'Emergency Contact';

  @override
  String get profileLabelEmergencyPhone => 'Emergency Phone';

  @override
  String get profileStatusActive => 'Active';

  @override
  String get profileNotAvailable => 'N/A';

  @override
  String get profileActionChangePassword => 'Change Password';

  @override
  String get profileActionHelp => 'Help & Support';

  @override
  String get profileActionAbout => 'About';

  @override
  String get profileHelpToast => 'Contact your property manager for assistance';

  @override
  String get changePasswordTitle => 'Change Password';

  @override
  String get changePasswordCurrent => 'Current Password';

  @override
  String get changePasswordNew => 'New Password';

  @override
  String get changePasswordConfirm => 'Confirm New Password';

  @override
  String get changePasswordSubmit => 'Update Password';

  @override
  String get changePasswordSuccess => 'Password changed';

  @override
  String get changePasswordMismatch => 'Passwords don\'t match';

  @override
  String get makePayment => 'Make Payment';

  @override
  String get updateNow => 'Update Now';

  @override
  String get submitTransfer => 'Submit Transfer';

  @override
  String get backToDashboard => 'Back to Dashboard';

  @override
  String get changeAction => 'Change';

  @override
  String get changePasswordFailed => 'Failed to change password';

  @override
  String aboutVersion(String version) {
    return 'Version $version';
  }

  @override
  String get aboutTagline => 'VIIJAR Rent Collection System - Tenant Mobile App';

  @override
  String get aboutDescription => 'Manage your rentals, make payments, and submit maintenance requests all in one place.';

  @override
  String get paymentViewReceipt => 'View receipt';

  @override
  String get paymentSelectAndPay => 'Select & Pay';

  @override
  String get paymentPayNow => 'Pay Now';

  @override
  String get paymentKnetSubtitle => 'Pay online instantly using KNET';

  @override
  String get paymentBankTransfer => 'Bank Transfer';

  @override
  String get paymentBankTransferSubtitle => 'Upload your transfer receipt';

  @override
  String get paymentUploadReceipt => 'Upload Receipt';

  @override
  String get paymentAuthError => 'Authentication error. Please log in again.';

  @override
  String get paymentRedirectingKnet => 'Redirecting to KNET payment gateway...';

  @override
  String get paymentInitiateFailed => 'Failed to initiate payment';

  @override
  String get paymentSubmitBankTransferTitle => 'Submit Bank Transfer';

  @override
  String get paymentBankInstructionsTitle => 'Bank Transfer Instructions';

  @override
  String get paymentBankInstructionsBody => '1. Make the transfer to our bank account\n2. Take a photo/screenshot of the transfer receipt\n3. Fill in the details below\n4. Your payment will be verified by our team';

  @override
  String get paymentAmountKwd => 'Amount (KWD)';

  @override
  String get paymentTransferDate => 'Transfer Date';

  @override
  String get paymentBankName => 'Bank Name *';

  @override
  String get paymentSelectBankValidation => 'Please select a bank';

  @override
  String get paymentReferenceOptional => 'Reference Number (Optional)';

  @override
  String get paymentNotesOptional => 'Notes (Optional)';

  @override
  String get paymentTransferProof => 'Transfer proof *';

  @override
  String get paymentAttachProofFirst => 'Attach the transfer proof (receipt photo) first.';

  @override
  String get paymentBankTransferSuccess => 'Bank transfer submitted successfully! Awaiting verification.';

  @override
  String get paymentSubmitTransferFailed => 'Failed to submit transfer';

  @override
  String get paymentSelectInvoice => 'Select Invoice to Pay';

  @override
  String get paymentChooseMethod => 'Choose Payment Method';

  @override
  String get paymentInvoiceAmount => 'Invoice Amount:';

  @override
  String get paymentCash => 'Cash Payment';

  @override
  String get paymentCashDescription => 'Pay at the management office. Quote the reference below so we apply your cash to the right invoice.';

  @override
  String get paymentCashOfficeHours => 'Office hours: Sun–Thu, 9 AM – 5 PM. A receipt is issued once the payment is verified.';

  @override
  String get paymentNoPendingInvoices => 'No Pending Invoices';

  @override
  String get paymentNoPendingInvoicesBody => 'You don\'t have any unpaid invoices at the moment.';

  @override
  String get paymentErrorLoadingInvoices => 'Error loading invoices';

  @override
  String invoiceNumberShort(String id) {
    return 'Invoice #$id';
  }

  @override
  String dueColonLabel(String date) {
    return 'Due: $date';
  }

  @override
  String get labelProperty => 'Property';

  @override
  String get labelUnit => 'Unit';

  @override
  String get notAvailable => 'N/A';

  @override
  String get dashboardFailedToLoad => 'Failed to load data';

  @override
  String get leasesFailedToLoad => 'Failed to load leases';

  @override
  String get leasesEmptyTitle => 'No properties found';

  @override
  String get leasesEmptyHint => 'You currently have no active leases';

  @override
  String get forceUpdateTitle => 'Update Required';

  @override
  String get forceUpdateBody => 'This version is no longer supported. Please update to the latest version to continue.';

  @override
  String get sessionRelogin => 'Please log in again.';

  @override
  String invoiceDownloadFailed(String code) {
    return 'Could not download invoice (HTTP $code).';
  }

  @override
  String receiptDownloadFailed(String code) {
    return 'Could not download receipt (HTTP $code).';
  }

  @override
  String pdfOpenFailed(String message) {
    return 'Could not open PDF: $message';
  }

  @override
  String invoicePdfError(String error) {
    return 'Invoice PDF error: $error';
  }

  @override
  String receiptError(String error) {
    return 'Receipt error: $error';
  }

  @override
  String get labelCategory => 'Category';

  @override
  String get labelPriority => 'Priority';

  @override
  String get labelRequestDate => 'Request Date';

  @override
  String get labelCompletedDate => 'Completed Date';

  @override
  String get labelAssignedTo => 'Assigned To';

  @override
  String get labelDescription => 'Description';

  @override
  String get labelNotes => 'Notes';

  @override
  String requestNumberShort(String id) {
    return 'Request #$id';
  }

  @override
  String requestedOnLabel(String date) {
    return 'Requested on $date';
  }

  @override
  String get labelUnitNumber => 'Unit Number';

  @override
  String get labelMonthlyRent => 'Monthly Rent';

  @override
  String get labelSecurityDeposit => 'Security Deposit';

  @override
  String get labelStartDate => 'Start Date';

  @override
  String get labelEndDate => 'End Date';

  @override
  String get labelDaysRemaining => 'Days Remaining';

  @override
  String get labelPaymentDueDay => 'Payment Due Day';

  @override
  String get statusExpired => 'Expired';

  @override
  String get statusExpiringSoon => 'Expiring Soon';

  @override
  String daysValue(String days) {
    return '$days days';
  }

  @override
  String paymentDueDayValue(String day) {
    return 'Day $day of each month';
  }

  @override
  String get statusPending => 'Pending';

  @override
  String get statusPaid => 'Paid';

  @override
  String get statusPartiallyPaid => 'Partially Paid';

  @override
  String get statusOverdue => 'Overdue';

  @override
  String get statusCancelled => 'Cancelled';

  @override
  String get statusCompleted => 'Completed';

  @override
  String get statusFailed => 'Failed';

  @override
  String get statusRefunded => 'Refunded';

  @override
  String get statusActive => 'Active';

  @override
  String get statusTerminated => 'Terminated';

  @override
  String get statusInProgress => 'In Progress';

  @override
  String get statusApproved => 'Approved';

  @override
  String get statusRejected => 'Rejected';

  @override
  String get statusOpen => 'Open';

  @override
  String get statusAssigned => 'Assigned';

  @override
  String get payInvoicesInOrder => 'Pay invoices in order. ';

  @override
  String startWithInvoiceHint(String id, String count) {
    return 'Start with invoice #$id. The remaining $count will unlock once it\'s fully paid.';
  }

  @override
  String payInvoiceFirst(String id) {
    return 'Pay invoice #$id first';
  }
}
