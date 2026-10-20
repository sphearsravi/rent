import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_ar.dart';
import 'app_localizations_en.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale) : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations? of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations);
  }

  static const LocalizationsDelegate<AppLocalizations> delegate = _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates = <LocalizationsDelegate<dynamic>>[
    delegate,
    GlobalMaterialLocalizations.delegate,
    GlobalCupertinoLocalizations.delegate,
    GlobalWidgetsLocalizations.delegate,
  ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('ar'),
    Locale('en')
  ];

  /// Application display name
  ///
  /// In en, this message translates to:
  /// **'Alrtam'**
  String get appName;

  /// No description provided for @loginTitle.
  ///
  /// In en, this message translates to:
  /// **'Sign In'**
  String get loginTitle;

  /// No description provided for @loginCivilIdLabel.
  ///
  /// In en, this message translates to:
  /// **'Civil ID'**
  String get loginCivilIdLabel;

  /// No description provided for @loginCivilIdHint.
  ///
  /// In en, this message translates to:
  /// **'Enter your Civil ID'**
  String get loginCivilIdHint;

  /// No description provided for @loginCivilIdRequired.
  ///
  /// In en, this message translates to:
  /// **'Please enter your Civil ID'**
  String get loginCivilIdRequired;

  /// No description provided for @loginCivilIdInvalid.
  ///
  /// In en, this message translates to:
  /// **'Civil ID must be 12 digits starting with 2 or 3.'**
  String get loginCivilIdInvalid;

  /// No description provided for @loginPasswordLabel.
  ///
  /// In en, this message translates to:
  /// **'Password'**
  String get loginPasswordLabel;

  /// No description provided for @loginPasswordHint.
  ///
  /// In en, this message translates to:
  /// **'Enter your password'**
  String get loginPasswordHint;

  /// No description provided for @loginPasswordRequired.
  ///
  /// In en, this message translates to:
  /// **'Please enter your password'**
  String get loginPasswordRequired;

  /// No description provided for @loginRememberMe.
  ///
  /// In en, this message translates to:
  /// **'Remember me for 30 days'**
  String get loginRememberMe;

  /// No description provided for @loginInvalidCredentials.
  ///
  /// In en, this message translates to:
  /// **'Invalid Civil ID or password.'**
  String get loginInvalidCredentials;

  /// No description provided for @loginContactAdmin.
  ///
  /// In en, this message translates to:
  /// **'If you have trouble signing in, contact your property manager.'**
  String get loginContactAdmin;

  /// No description provided for @loginSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Sign in with your Civil ID'**
  String get loginSubtitle;

  /// No description provided for @loginTenantPortal.
  ///
  /// In en, this message translates to:
  /// **'Tenant Portal'**
  String get loginTenantPortal;

  /// No description provided for @loginNeedHelp.
  ///
  /// In en, this message translates to:
  /// **'Need help? Contact your property manager'**
  String get loginNeedHelp;

  /// No description provided for @logout.
  ///
  /// In en, this message translates to:
  /// **'Logout'**
  String get logout;

  /// No description provided for @logoutConfirmTitle.
  ///
  /// In en, this message translates to:
  /// **'Logout'**
  String get logoutConfirmTitle;

  /// No description provided for @logoutConfirmMessage.
  ///
  /// In en, this message translates to:
  /// **'Are you sure you want to log out?'**
  String get logoutConfirmMessage;

  /// No description provided for @cancel.
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get cancel;

  /// No description provided for @retry.
  ///
  /// In en, this message translates to:
  /// **'Retry'**
  String get retry;

  /// No description provided for @submit.
  ///
  /// In en, this message translates to:
  /// **'Submit'**
  String get submit;

  /// No description provided for @close.
  ///
  /// In en, this message translates to:
  /// **'Close'**
  String get close;

  /// No description provided for @done.
  ///
  /// In en, this message translates to:
  /// **'Done'**
  String get done;

  /// No description provided for @checkAgain.
  ///
  /// In en, this message translates to:
  /// **'Check Again'**
  String get checkAgain;

  /// No description provided for @refresh.
  ///
  /// In en, this message translates to:
  /// **'Refresh'**
  String get refresh;

  /// No description provided for @errorGeneric.
  ///
  /// In en, this message translates to:
  /// **'Something went wrong. Please try again.'**
  String get errorGeneric;

  /// No description provided for @errorNetwork.
  ///
  /// In en, this message translates to:
  /// **'Unable to connect. Check your internet connection.'**
  String get errorNetwork;

  /// No description provided for @errorTimeout.
  ///
  /// In en, this message translates to:
  /// **'The request timed out. Please try again.'**
  String get errorTimeout;

  /// No description provided for @errorSessionExpired.
  ///
  /// In en, this message translates to:
  /// **'Session expired. Please log in again.'**
  String get errorSessionExpired;

  /// No description provided for @errorForbidden.
  ///
  /// In en, this message translates to:
  /// **'You don\'t have permission to do this.'**
  String get errorForbidden;

  /// No description provided for @errorNotFound.
  ///
  /// In en, this message translates to:
  /// **'We couldn\'t find what you were looking for.'**
  String get errorNotFound;

  /// No description provided for @errorServer.
  ///
  /// In en, this message translates to:
  /// **'Server is having trouble. Please try again later.'**
  String get errorServer;

  /// No description provided for @paymentStatusPending.
  ///
  /// In en, this message translates to:
  /// **'Pending'**
  String get paymentStatusPending;

  /// No description provided for @paymentStatusCompleted.
  ///
  /// In en, this message translates to:
  /// **'Completed'**
  String get paymentStatusCompleted;

  /// No description provided for @paymentStatusFailed.
  ///
  /// In en, this message translates to:
  /// **'Failed'**
  String get paymentStatusFailed;

  /// No description provided for @paymentStatusCancelled.
  ///
  /// In en, this message translates to:
  /// **'Cancelled'**
  String get paymentStatusCancelled;

  /// No description provided for @invoiceStatusPending.
  ///
  /// In en, this message translates to:
  /// **'Pending'**
  String get invoiceStatusPending;

  /// No description provided for @invoiceStatusPartiallyPaid.
  ///
  /// In en, this message translates to:
  /// **'Partially Paid'**
  String get invoiceStatusPartiallyPaid;

  /// No description provided for @invoiceStatusPaid.
  ///
  /// In en, this message translates to:
  /// **'Paid'**
  String get invoiceStatusPaid;

  /// No description provided for @invoiceStatusOverdue.
  ///
  /// In en, this message translates to:
  /// **'Overdue'**
  String get invoiceStatusOverdue;

  /// No description provided for @navDashboard.
  ///
  /// In en, this message translates to:
  /// **'Dashboard'**
  String get navDashboard;

  /// No description provided for @navPayments.
  ///
  /// In en, this message translates to:
  /// **'Payments'**
  String get navPayments;

  /// No description provided for @navInvoices.
  ///
  /// In en, this message translates to:
  /// **'Invoices'**
  String get navInvoices;

  /// No description provided for @navLeases.
  ///
  /// In en, this message translates to:
  /// **'Leases'**
  String get navLeases;

  /// No description provided for @navMaintenance.
  ///
  /// In en, this message translates to:
  /// **'Maintenance'**
  String get navMaintenance;

  /// No description provided for @navProfile.
  ///
  /// In en, this message translates to:
  /// **'Profile'**
  String get navProfile;

  /// No description provided for @greetingMorning.
  ///
  /// In en, this message translates to:
  /// **'Good morning,'**
  String get greetingMorning;

  /// No description provided for @greetingAfternoon.
  ///
  /// In en, this message translates to:
  /// **'Good afternoon,'**
  String get greetingAfternoon;

  /// No description provided for @greetingEvening.
  ///
  /// In en, this message translates to:
  /// **'Good evening,'**
  String get greetingEvening;

  /// No description provided for @dashboardSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Here\'s your rental dashboard'**
  String get dashboardSubtitle;

  /// No description provided for @dashboardTenantFallback.
  ///
  /// In en, this message translates to:
  /// **'Tenant'**
  String get dashboardTenantFallback;

  /// No description provided for @statActiveLeases.
  ///
  /// In en, this message translates to:
  /// **'Active Leases'**
  String get statActiveLeases;

  /// No description provided for @statTotalPayments.
  ///
  /// In en, this message translates to:
  /// **'Total Payments'**
  String get statTotalPayments;

  /// No description provided for @statPendingRequests.
  ///
  /// In en, this message translates to:
  /// **'Pending Requests'**
  String get statPendingRequests;

  /// No description provided for @dashboardMyProperties.
  ///
  /// In en, this message translates to:
  /// **'My Properties'**
  String get dashboardMyProperties;

  /// No description provided for @dashboardViewInvoices.
  ///
  /// In en, this message translates to:
  /// **'View Invoices'**
  String get dashboardViewInvoices;

  /// No description provided for @dashboardNoLeases.
  ///
  /// In en, this message translates to:
  /// **'No active leases found'**
  String get dashboardNoLeases;

  /// No description provided for @dashboardRecentPayments.
  ///
  /// In en, this message translates to:
  /// **'Recent Payments'**
  String get dashboardRecentPayments;

  /// No description provided for @dashboardNoPayments.
  ///
  /// In en, this message translates to:
  /// **'No payments found'**
  String get dashboardNoPayments;

  /// No description provided for @dashboardMaintenanceTitle.
  ///
  /// In en, this message translates to:
  /// **'Maintenance Requests'**
  String get dashboardMaintenanceTitle;

  /// No description provided for @dashboardNewRequest.
  ///
  /// In en, this message translates to:
  /// **'New Request'**
  String get dashboardNewRequest;

  /// No description provided for @dashboardNoMaintenance.
  ///
  /// In en, this message translates to:
  /// **'No pending maintenance requests'**
  String get dashboardNoMaintenance;

  /// No description provided for @dashboardAmountDue.
  ///
  /// In en, this message translates to:
  /// **'Amount due'**
  String get dashboardAmountDue;

  /// No description provided for @dashboardAllPaidUp.
  ///
  /// In en, this message translates to:
  /// **'You\'re all paid up'**
  String get dashboardAllPaidUp;

  /// No description provided for @dashboardAllPaidUpBody.
  ///
  /// In en, this message translates to:
  /// **'No outstanding invoices — nothing to pay right now.'**
  String get dashboardAllPaidUpBody;

  /// No description provided for @dashboardOverdueChip.
  ///
  /// In en, this message translates to:
  /// **'{count} overdue'**
  String dashboardOverdueChip(int count);

  /// No description provided for @dashboardNextPaymentDue.
  ///
  /// In en, this message translates to:
  /// **'Next payment due'**
  String get dashboardNextPaymentDue;

  /// No description provided for @dashboardDueToday.
  ///
  /// In en, this message translates to:
  /// **'Due today'**
  String get dashboardDueToday;

  /// No description provided for @dashboardDueInDays.
  ///
  /// In en, this message translates to:
  /// **'Due in {days} days'**
  String dashboardDueInDays(int days);

  /// No description provided for @dashboardOverdueByDays.
  ///
  /// In en, this message translates to:
  /// **'Overdue by {days} days'**
  String dashboardOverdueByDays(int days);

  /// No description provided for @dashboardPayNow.
  ///
  /// In en, this message translates to:
  /// **'Pay now'**
  String get dashboardPayNow;

  /// No description provided for @dashboardQuickActions.
  ///
  /// In en, this message translates to:
  /// **'Quick actions'**
  String get dashboardQuickActions;

  /// No description provided for @dashboardActionPay.
  ///
  /// In en, this message translates to:
  /// **'Pay'**
  String get dashboardActionPay;

  /// No description provided for @dashboardActionRequest.
  ///
  /// In en, this message translates to:
  /// **'New request'**
  String get dashboardActionRequest;

  /// No description provided for @dashboardActionLeases.
  ///
  /// In en, this message translates to:
  /// **'My leases'**
  String get dashboardActionLeases;

  /// No description provided for @dashboardActionProfile.
  ///
  /// In en, this message translates to:
  /// **'Profile'**
  String get dashboardActionProfile;

  /// No description provided for @dashboardStatOutstanding.
  ///
  /// In en, this message translates to:
  /// **'Outstanding'**
  String get dashboardStatOutstanding;

  /// No description provided for @dashboardStatOverdue.
  ///
  /// In en, this message translates to:
  /// **'Overdue'**
  String get dashboardStatOverdue;

  /// No description provided for @dashboardStatOpenRequests.
  ///
  /// In en, this message translates to:
  /// **'Open requests'**
  String get dashboardStatOpenRequests;

  /// No description provided for @dashboardStatMonthlyRent.
  ///
  /// In en, this message translates to:
  /// **'Monthly rent'**
  String get dashboardStatMonthlyRent;

  /// No description provided for @dashboardLeaseExpiring.
  ///
  /// In en, this message translates to:
  /// **'Lease expires in {days} days'**
  String dashboardLeaseExpiring(int days);

  /// No description provided for @dashboardLeaseExpiringAction.
  ///
  /// In en, this message translates to:
  /// **'Contact office to renew'**
  String get dashboardLeaseExpiringAction;

  /// No description provided for @dashboardInvoicesDueOne.
  ///
  /// In en, this message translates to:
  /// **'1 invoice to pay'**
  String get dashboardInvoicesDueOne;

  /// No description provided for @dashboardInvoicesDueMany.
  ///
  /// In en, this message translates to:
  /// **'{count} invoices to pay'**
  String dashboardInvoicesDueMany(int count);

  /// No description provided for @invoicesTitle.
  ///
  /// In en, this message translates to:
  /// **'Invoices'**
  String get invoicesTitle;

  /// No description provided for @invoicesFilterAll.
  ///
  /// In en, this message translates to:
  /// **'All'**
  String get invoicesFilterAll;

  /// No description provided for @invoicesFilterOutstanding.
  ///
  /// In en, this message translates to:
  /// **'Outstanding'**
  String get invoicesFilterOutstanding;

  /// No description provided for @invoicesFilterOverdue.
  ///
  /// In en, this message translates to:
  /// **'Overdue'**
  String get invoicesFilterOverdue;

  /// No description provided for @invoicesFilterPaid.
  ///
  /// In en, this message translates to:
  /// **'Paid'**
  String get invoicesFilterPaid;

  /// No description provided for @invoicesEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'No invoices to show'**
  String get invoicesEmptyTitle;

  /// No description provided for @invoicesEmptyHint.
  ///
  /// In en, this message translates to:
  /// **'Pull down to refresh'**
  String get invoicesEmptyHint;

  /// No description provided for @invoicesErrorTitle.
  ///
  /// In en, this message translates to:
  /// **'Error loading invoices'**
  String get invoicesErrorTitle;

  /// No description provided for @invoicesFailedToLoad.
  ///
  /// In en, this message translates to:
  /// **'Failed to load invoices'**
  String get invoicesFailedToLoad;

  /// No description provided for @invoiceDetailTitle.
  ///
  /// In en, this message translates to:
  /// **'Invoice #{id}'**
  String invoiceDetailTitle(int id);

  /// No description provided for @invoiceUnit.
  ///
  /// In en, this message translates to:
  /// **'Unit {unit}'**
  String invoiceUnit(String unit);

  /// No description provided for @invoiceTypeLabel.
  ///
  /// In en, this message translates to:
  /// **'Type'**
  String get invoiceTypeLabel;

  /// No description provided for @invoiceTypeRent.
  ///
  /// In en, this message translates to:
  /// **'Rent'**
  String get invoiceTypeRent;

  /// No description provided for @invoiceDateLabel.
  ///
  /// In en, this message translates to:
  /// **'Invoice date'**
  String get invoiceDateLabel;

  /// No description provided for @invoiceDueDateLabel.
  ///
  /// In en, this message translates to:
  /// **'Due date'**
  String get invoiceDueDateLabel;

  /// No description provided for @invoiceTotalLabel.
  ///
  /// In en, this message translates to:
  /// **'Total'**
  String get invoiceTotalLabel;

  /// No description provided for @invoicePaidLabel.
  ///
  /// In en, this message translates to:
  /// **'Paid'**
  String get invoicePaidLabel;

  /// No description provided for @invoiceBalanceLabel.
  ///
  /// In en, this message translates to:
  /// **'Balance'**
  String get invoiceBalanceLabel;

  /// No description provided for @invoicePaymentsLabel.
  ///
  /// In en, this message translates to:
  /// **'Payments'**
  String get invoicePaymentsLabel;

  /// No description provided for @invoiceNoPayments.
  ///
  /// In en, this message translates to:
  /// **'No payments recorded yet.'**
  String get invoiceNoPayments;

  /// No description provided for @invoiceReceipt.
  ///
  /// In en, this message translates to:
  /// **'Receipt'**
  String get invoiceReceipt;

  /// No description provided for @invoicePayNow.
  ///
  /// In en, this message translates to:
  /// **'Pay now'**
  String get invoicePayNow;

  /// No description provided for @invoiceDownloadPdf.
  ///
  /// In en, this message translates to:
  /// **'Download PDF'**
  String get invoiceDownloadPdf;

  /// No description provided for @invoiceNotFound.
  ///
  /// In en, this message translates to:
  /// **'Invoice not found'**
  String get invoiceNotFound;

  /// No description provided for @invoiceFailedToLoad.
  ///
  /// In en, this message translates to:
  /// **'Failed to load invoice'**
  String get invoiceFailedToLoad;

  /// No description provided for @paymentStatusScreenTitle.
  ///
  /// In en, this message translates to:
  /// **'Payment Status'**
  String get paymentStatusScreenTitle;

  /// No description provided for @paymentChecking.
  ///
  /// In en, this message translates to:
  /// **'Checking payment status...'**
  String get paymentChecking;

  /// No description provided for @paymentSuccessful.
  ///
  /// In en, this message translates to:
  /// **'Payment Successful!'**
  String get paymentSuccessful;

  /// No description provided for @paymentResultFailed.
  ///
  /// In en, this message translates to:
  /// **'Payment Failed'**
  String get paymentResultFailed;

  /// No description provided for @paymentProcessing.
  ///
  /// In en, this message translates to:
  /// **'Payment is being processed...'**
  String get paymentProcessing;

  /// No description provided for @paymentStillProcessing.
  ///
  /// In en, this message translates to:
  /// **'Payment is still being processed. You can check again in a moment.'**
  String get paymentStillProcessing;

  /// No description provided for @paymentUnableToVerify.
  ///
  /// In en, this message translates to:
  /// **'Unable to verify payment status. Check the invoice for the latest status.'**
  String get paymentUnableToVerify;

  /// No description provided for @paymentUnableToVerifyShort.
  ///
  /// In en, this message translates to:
  /// **'Unable to verify payment status'**
  String get paymentUnableToVerifyShort;

  /// No description provided for @paymentInvalidReference.
  ///
  /// In en, this message translates to:
  /// **'Invalid payment reference'**
  String get paymentInvalidReference;

  /// No description provided for @paymentReferenceLabel.
  ///
  /// In en, this message translates to:
  /// **'Reference: '**
  String get paymentReferenceLabel;

  /// No description provided for @maintenanceTitle.
  ///
  /// In en, this message translates to:
  /// **'Maintenance Requests'**
  String get maintenanceTitle;

  /// No description provided for @maintenanceEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'No maintenance requests'**
  String get maintenanceEmptyTitle;

  /// No description provided for @maintenanceEmptyHint.
  ///
  /// In en, this message translates to:
  /// **'Submit a request if you need repairs'**
  String get maintenanceEmptyHint;

  /// No description provided for @maintenanceNewRequest.
  ///
  /// In en, this message translates to:
  /// **'New Request'**
  String get maintenanceNewRequest;

  /// No description provided for @maintenanceFailedToLoad.
  ///
  /// In en, this message translates to:
  /// **'Failed to load requests'**
  String get maintenanceFailedToLoad;

  /// No description provided for @maintenanceCreateTitle.
  ///
  /// In en, this message translates to:
  /// **'New Maintenance Request'**
  String get maintenanceCreateTitle;

  /// No description provided for @maintenancePropertyUnit.
  ///
  /// In en, this message translates to:
  /// **'Property/Unit'**
  String get maintenancePropertyUnit;

  /// No description provided for @maintenanceSelectProperty.
  ///
  /// In en, this message translates to:
  /// **'Select a property...'**
  String get maintenanceSelectProperty;

  /// No description provided for @maintenanceSelectPropertyError.
  ///
  /// In en, this message translates to:
  /// **'Please select a property'**
  String get maintenanceSelectPropertyError;

  /// No description provided for @maintenanceCategory.
  ///
  /// In en, this message translates to:
  /// **'Category'**
  String get maintenanceCategory;

  /// No description provided for @maintenanceSelectCategory.
  ///
  /// In en, this message translates to:
  /// **'Select a category...'**
  String get maintenanceSelectCategory;

  /// No description provided for @maintenanceSelectCategoryError.
  ///
  /// In en, this message translates to:
  /// **'Please select a category'**
  String get maintenanceSelectCategoryError;

  /// No description provided for @maintenancePriority.
  ///
  /// In en, this message translates to:
  /// **'Priority'**
  String get maintenancePriority;

  /// No description provided for @maintenanceSelectPriority.
  ///
  /// In en, this message translates to:
  /// **'Select priority...'**
  String get maintenanceSelectPriority;

  /// No description provided for @maintenanceSelectPriorityError.
  ///
  /// In en, this message translates to:
  /// **'Please select a priority'**
  String get maintenanceSelectPriorityError;

  /// No description provided for @maintenanceDescription.
  ///
  /// In en, this message translates to:
  /// **'Description'**
  String get maintenanceDescription;

  /// No description provided for @maintenanceDescriptionHint.
  ///
  /// In en, this message translates to:
  /// **'Please describe the issue in detail...'**
  String get maintenanceDescriptionHint;

  /// No description provided for @maintenanceDescriptionError.
  ///
  /// In en, this message translates to:
  /// **'Please describe the issue'**
  String get maintenanceDescriptionError;

  /// No description provided for @maintenancePhotosOptional.
  ///
  /// In en, this message translates to:
  /// **'Photos (optional)'**
  String get maintenancePhotosOptional;

  /// No description provided for @maintenancePhotosHint.
  ///
  /// In en, this message translates to:
  /// **'Add up to 5 photos. Camera or gallery.'**
  String get maintenancePhotosHint;

  /// No description provided for @maintenanceCamera.
  ///
  /// In en, this message translates to:
  /// **'Camera'**
  String get maintenanceCamera;

  /// No description provided for @maintenanceGallery.
  ///
  /// In en, this message translates to:
  /// **'Gallery'**
  String get maintenanceGallery;

  /// No description provided for @maintenanceSubmitRequest.
  ///
  /// In en, this message translates to:
  /// **'Submit Request'**
  String get maintenanceSubmitRequest;

  /// No description provided for @maintenanceSubmitting.
  ///
  /// In en, this message translates to:
  /// **'Submitting...'**
  String get maintenanceSubmitting;

  /// No description provided for @maintenanceSubmitted.
  ///
  /// In en, this message translates to:
  /// **'Maintenance request submitted successfully!'**
  String get maintenanceSubmitted;

  /// No description provided for @profileTitle.
  ///
  /// In en, this message translates to:
  /// **'My Profile'**
  String get profileTitle;

  /// No description provided for @profileFailedToLoad.
  ///
  /// In en, this message translates to:
  /// **'Failed to load profile'**
  String get profileFailedToLoad;

  /// No description provided for @language.
  ///
  /// In en, this message translates to:
  /// **'Language'**
  String get language;

  /// No description provided for @profileSectionPersonal.
  ///
  /// In en, this message translates to:
  /// **'Personal Information'**
  String get profileSectionPersonal;

  /// No description provided for @profileLabelCivilId.
  ///
  /// In en, this message translates to:
  /// **'Civil ID'**
  String get profileLabelCivilId;

  /// No description provided for @profileLabelPhone.
  ///
  /// In en, this message translates to:
  /// **'Phone'**
  String get profileLabelPhone;

  /// No description provided for @profileLabelEmail.
  ///
  /// In en, this message translates to:
  /// **'Email'**
  String get profileLabelEmail;

  /// No description provided for @profileLabelDob.
  ///
  /// In en, this message translates to:
  /// **'Date of Birth'**
  String get profileLabelDob;

  /// No description provided for @profileLabelCompany.
  ///
  /// In en, this message translates to:
  /// **'Company'**
  String get profileLabelCompany;

  /// No description provided for @profileLabelEmergencyContact.
  ///
  /// In en, this message translates to:
  /// **'Emergency Contact'**
  String get profileLabelEmergencyContact;

  /// No description provided for @profileLabelEmergencyPhone.
  ///
  /// In en, this message translates to:
  /// **'Emergency Phone'**
  String get profileLabelEmergencyPhone;

  /// No description provided for @profileStatusActive.
  ///
  /// In en, this message translates to:
  /// **'Active'**
  String get profileStatusActive;

  /// No description provided for @profileNotAvailable.
  ///
  /// In en, this message translates to:
  /// **'N/A'**
  String get profileNotAvailable;

  /// No description provided for @profileActionChangePassword.
  ///
  /// In en, this message translates to:
  /// **'Change Password'**
  String get profileActionChangePassword;

  /// No description provided for @profileActionHelp.
  ///
  /// In en, this message translates to:
  /// **'Help & Support'**
  String get profileActionHelp;

  /// No description provided for @profileActionAbout.
  ///
  /// In en, this message translates to:
  /// **'About'**
  String get profileActionAbout;

  /// No description provided for @profileHelpToast.
  ///
  /// In en, this message translates to:
  /// **'Contact your property manager for assistance'**
  String get profileHelpToast;

  /// No description provided for @changePasswordTitle.
  ///
  /// In en, this message translates to:
  /// **'Change Password'**
  String get changePasswordTitle;

  /// No description provided for @changePasswordCurrent.
  ///
  /// In en, this message translates to:
  /// **'Current Password'**
  String get changePasswordCurrent;

  /// No description provided for @changePasswordNew.
  ///
  /// In en, this message translates to:
  /// **'New Password'**
  String get changePasswordNew;

  /// No description provided for @changePasswordConfirm.
  ///
  /// In en, this message translates to:
  /// **'Confirm New Password'**
  String get changePasswordConfirm;

  /// No description provided for @changePasswordSubmit.
  ///
  /// In en, this message translates to:
  /// **'Update Password'**
  String get changePasswordSubmit;

  /// No description provided for @changePasswordSuccess.
  ///
  /// In en, this message translates to:
  /// **'Password changed'**
  String get changePasswordSuccess;

  /// No description provided for @changePasswordMismatch.
  ///
  /// In en, this message translates to:
  /// **'Passwords don\'t match'**
  String get changePasswordMismatch;

  /// No description provided for @makePayment.
  ///
  /// In en, this message translates to:
  /// **'Make Payment'**
  String get makePayment;

  /// No description provided for @updateNow.
  ///
  /// In en, this message translates to:
  /// **'Update Now'**
  String get updateNow;

  /// No description provided for @submitTransfer.
  ///
  /// In en, this message translates to:
  /// **'Submit Transfer'**
  String get submitTransfer;

  /// No description provided for @backToDashboard.
  ///
  /// In en, this message translates to:
  /// **'Back to Dashboard'**
  String get backToDashboard;

  /// No description provided for @changeAction.
  ///
  /// In en, this message translates to:
  /// **'Change'**
  String get changeAction;

  /// No description provided for @changePasswordFailed.
  ///
  /// In en, this message translates to:
  /// **'Failed to change password'**
  String get changePasswordFailed;

  /// No description provided for @aboutVersion.
  ///
  /// In en, this message translates to:
  /// **'Version {version}'**
  String aboutVersion(String version);

  /// No description provided for @aboutTagline.
  ///
  /// In en, this message translates to:
  /// **'Alrtam Rent Collection System - Tenant Mobile App'**
  String get aboutTagline;

  /// No description provided for @aboutDescription.
  ///
  /// In en, this message translates to:
  /// **'Manage your rentals, make payments, and submit maintenance requests all in one place.'**
  String get aboutDescription;

  /// No description provided for @paymentViewReceipt.
  ///
  /// In en, this message translates to:
  /// **'View receipt'**
  String get paymentViewReceipt;

  /// No description provided for @paymentSelectAndPay.
  ///
  /// In en, this message translates to:
  /// **'Select & Pay'**
  String get paymentSelectAndPay;

  /// No description provided for @paymentPayNow.
  ///
  /// In en, this message translates to:
  /// **'Pay Now'**
  String get paymentPayNow;

  /// No description provided for @paymentKnetSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Pay online instantly using KNET'**
  String get paymentKnetSubtitle;

  /// No description provided for @paymentBankTransfer.
  ///
  /// In en, this message translates to:
  /// **'Bank Transfer'**
  String get paymentBankTransfer;

  /// No description provided for @paymentBankTransferSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Upload your transfer receipt'**
  String get paymentBankTransferSubtitle;

  /// No description provided for @paymentUploadReceipt.
  ///
  /// In en, this message translates to:
  /// **'Upload Receipt'**
  String get paymentUploadReceipt;

  /// No description provided for @paymentAuthError.
  ///
  /// In en, this message translates to:
  /// **'Authentication error. Please log in again.'**
  String get paymentAuthError;

  /// No description provided for @paymentRedirectingKnet.
  ///
  /// In en, this message translates to:
  /// **'Redirecting to KNET payment gateway...'**
  String get paymentRedirectingKnet;

  /// No description provided for @paymentInitiateFailed.
  ///
  /// In en, this message translates to:
  /// **'Failed to initiate payment'**
  String get paymentInitiateFailed;

  /// No description provided for @paymentSubmitBankTransferTitle.
  ///
  /// In en, this message translates to:
  /// **'Submit Bank Transfer'**
  String get paymentSubmitBankTransferTitle;

  /// No description provided for @paymentBankInstructionsTitle.
  ///
  /// In en, this message translates to:
  /// **'Bank Transfer Instructions'**
  String get paymentBankInstructionsTitle;

  /// No description provided for @paymentBankInstructionsBody.
  ///
  /// In en, this message translates to:
  /// **'1. Make the transfer to our bank account\n2. Take a photo/screenshot of the transfer receipt\n3. Fill in the details below\n4. Your payment will be verified by our team'**
  String get paymentBankInstructionsBody;

  /// No description provided for @paymentAmountKwd.
  ///
  /// In en, this message translates to:
  /// **'Amount (KWD)'**
  String get paymentAmountKwd;

  /// No description provided for @paymentTransferDate.
  ///
  /// In en, this message translates to:
  /// **'Transfer Date'**
  String get paymentTransferDate;

  /// No description provided for @paymentBankName.
  ///
  /// In en, this message translates to:
  /// **'Bank Name *'**
  String get paymentBankName;

  /// No description provided for @paymentSelectBankValidation.
  ///
  /// In en, this message translates to:
  /// **'Please select a bank'**
  String get paymentSelectBankValidation;

  /// No description provided for @paymentReferenceOptional.
  ///
  /// In en, this message translates to:
  /// **'Reference Number (Optional)'**
  String get paymentReferenceOptional;

  /// No description provided for @paymentNotesOptional.
  ///
  /// In en, this message translates to:
  /// **'Notes (Optional)'**
  String get paymentNotesOptional;

  /// No description provided for @paymentTransferProof.
  ///
  /// In en, this message translates to:
  /// **'Transfer proof *'**
  String get paymentTransferProof;

  /// No description provided for @paymentAttachProofFirst.
  ///
  /// In en, this message translates to:
  /// **'Attach the transfer proof (receipt photo) first.'**
  String get paymentAttachProofFirst;

  /// No description provided for @paymentBankTransferSuccess.
  ///
  /// In en, this message translates to:
  /// **'Bank transfer submitted successfully! Awaiting verification.'**
  String get paymentBankTransferSuccess;

  /// No description provided for @paymentSubmitTransferFailed.
  ///
  /// In en, this message translates to:
  /// **'Failed to submit transfer'**
  String get paymentSubmitTransferFailed;

  /// No description provided for @paymentSelectInvoice.
  ///
  /// In en, this message translates to:
  /// **'Select Invoice to Pay'**
  String get paymentSelectInvoice;

  /// No description provided for @paymentChooseMethod.
  ///
  /// In en, this message translates to:
  /// **'Choose Payment Method'**
  String get paymentChooseMethod;

  /// No description provided for @paymentInvoiceAmount.
  ///
  /// In en, this message translates to:
  /// **'Invoice Amount:'**
  String get paymentInvoiceAmount;

  /// No description provided for @paymentCash.
  ///
  /// In en, this message translates to:
  /// **'Cash Payment'**
  String get paymentCash;

  /// No description provided for @paymentCashDescription.
  ///
  /// In en, this message translates to:
  /// **'Pay at the management office. Quote the reference below so we apply your cash to the right invoice.'**
  String get paymentCashDescription;

  /// No description provided for @paymentCashOfficeHours.
  ///
  /// In en, this message translates to:
  /// **'Office hours: Sun–Thu, 9 AM – 5 PM. A receipt is issued once the payment is verified.'**
  String get paymentCashOfficeHours;

  /// No description provided for @paymentCashRequest.
  ///
  /// In en, this message translates to:
  /// **'Notify office — I\'ll pay cash'**
  String get paymentCashRequest;

  /// No description provided for @paymentCashRequestSuccess.
  ///
  /// In en, this message translates to:
  /// **'The office has been notified. Pay at the counter and staff will confirm it.'**
  String get paymentCashRequestSuccess;

  /// No description provided for @paymentCashRequestFailed.
  ///
  /// In en, this message translates to:
  /// **'Couldn\'t send the cash request. Please try again.'**
  String get paymentCashRequestFailed;

  /// No description provided for @paymentCashRequestTitle.
  ///
  /// In en, this message translates to:
  /// **'Pay cash at the office'**
  String get paymentCashRequestTitle;

  /// No description provided for @paymentCashRequestIntro.
  ///
  /// In en, this message translates to:
  /// **'Tell the office how much you will bring. You can pay part of the balance now and the rest later.'**
  String get paymentCashRequestIntro;

  /// No description provided for @paymentCashRequestAmountLabel.
  ///
  /// In en, this message translates to:
  /// **'Amount I will pay (KWD)'**
  String get paymentCashRequestAmountLabel;

  /// No description provided for @paymentCashRequestAmountInvalid.
  ///
  /// In en, this message translates to:
  /// **'Enter an amount between 0.001 and {balance} KWD'**
  String paymentCashRequestAmountInvalid(String balance);

  /// No description provided for @paymentCashRequestPartialHint.
  ///
  /// In en, this message translates to:
  /// **'Remaining after this payment: {remaining} KWD'**
  String paymentCashRequestPartialHint(String remaining);

  /// No description provided for @paymentCashRequestFullBalance.
  ///
  /// In en, this message translates to:
  /// **'Full balance'**
  String get paymentCashRequestFullBalance;

  /// No description provided for @paymentCashRequestConfirm.
  ///
  /// In en, this message translates to:
  /// **'Notify office'**
  String get paymentCashRequestConfirm;

  /// No description provided for @errorInvalidCredentials.
  ///
  /// In en, this message translates to:
  /// **'Invalid Civil ID or password.'**
  String get errorInvalidCredentials;

  /// No description provided for @statusPendingVerification.
  ///
  /// In en, this message translates to:
  /// **'Pending verification'**
  String get statusPendingVerification;

  /// No description provided for @maintCatPlumbing.
  ///
  /// In en, this message translates to:
  /// **'Plumbing'**
  String get maintCatPlumbing;

  /// No description provided for @maintCatElectrical.
  ///
  /// In en, this message translates to:
  /// **'Electrical'**
  String get maintCatElectrical;

  /// No description provided for @maintCatHvac.
  ///
  /// In en, this message translates to:
  /// **'Air conditioning (HVAC)'**
  String get maintCatHvac;

  /// No description provided for @maintCatAppliance.
  ///
  /// In en, this message translates to:
  /// **'Appliance'**
  String get maintCatAppliance;

  /// No description provided for @maintCatStructural.
  ///
  /// In en, this message translates to:
  /// **'Structural'**
  String get maintCatStructural;

  /// No description provided for @maintCatOther.
  ///
  /// In en, this message translates to:
  /// **'Other'**
  String get maintCatOther;

  /// No description provided for @maintPrioLow.
  ///
  /// In en, this message translates to:
  /// **'Low'**
  String get maintPrioLow;

  /// No description provided for @maintPrioMedium.
  ///
  /// In en, this message translates to:
  /// **'Medium'**
  String get maintPrioMedium;

  /// No description provided for @maintPrioHigh.
  ///
  /// In en, this message translates to:
  /// **'High'**
  String get maintPrioHigh;

  /// No description provided for @maintPrioEmergency.
  ///
  /// In en, this message translates to:
  /// **'Emergency'**
  String get maintPrioEmergency;

  /// No description provided for @maintenanceSubmitFailed.
  ///
  /// In en, this message translates to:
  /// **'Couldn\'t submit the request. Please try again.'**
  String get maintenanceSubmitFailed;

  /// No description provided for @dashboardBalanceUnavailable.
  ///
  /// In en, this message translates to:
  /// **'Couldn\'t load your balance'**
  String get dashboardBalanceUnavailable;

  /// No description provided for @dashboardBalanceUnavailableBody.
  ///
  /// In en, this message translates to:
  /// **'Check your connection and pull down to refresh.'**
  String get dashboardBalanceUnavailableBody;

  /// No description provided for @paymentChooseMethodButton.
  ///
  /// In en, this message translates to:
  /// **'Choose payment method'**
  String get paymentChooseMethodButton;

  /// No description provided for @paymentForInvoice.
  ///
  /// In en, this message translates to:
  /// **'Payment for invoice #{id}'**
  String paymentForInvoice(String id);

  /// No description provided for @paymentAmountDueLabel.
  ///
  /// In en, this message translates to:
  /// **'Amount due'**
  String get paymentAmountDueLabel;

  /// No description provided for @payOldestFirstTitle.
  ///
  /// In en, this message translates to:
  /// **'Pay your oldest invoice first'**
  String get payOldestFirstTitle;

  /// No description provided for @payOldestFirstGoTo.
  ///
  /// In en, this message translates to:
  /// **'Go to invoice #{id}'**
  String payOldestFirstGoTo(String id);

  /// No description provided for @paymentNoPendingInvoices.
  ///
  /// In en, this message translates to:
  /// **'No Pending Invoices'**
  String get paymentNoPendingInvoices;

  /// No description provided for @paymentNoPendingInvoicesBody.
  ///
  /// In en, this message translates to:
  /// **'You don\'t have any unpaid invoices at the moment.'**
  String get paymentNoPendingInvoicesBody;

  /// No description provided for @paymentErrorLoadingInvoices.
  ///
  /// In en, this message translates to:
  /// **'Error loading invoices'**
  String get paymentErrorLoadingInvoices;

  /// No description provided for @invoiceNumberShort.
  ///
  /// In en, this message translates to:
  /// **'Invoice #{id}'**
  String invoiceNumberShort(String id);

  /// No description provided for @dueColonLabel.
  ///
  /// In en, this message translates to:
  /// **'Due: {date}'**
  String dueColonLabel(String date);

  /// No description provided for @labelProperty.
  ///
  /// In en, this message translates to:
  /// **'Property'**
  String get labelProperty;

  /// No description provided for @labelUnit.
  ///
  /// In en, this message translates to:
  /// **'Unit'**
  String get labelUnit;

  /// No description provided for @notAvailable.
  ///
  /// In en, this message translates to:
  /// **'N/A'**
  String get notAvailable;

  /// No description provided for @dashboardFailedToLoad.
  ///
  /// In en, this message translates to:
  /// **'Failed to load data'**
  String get dashboardFailedToLoad;

  /// No description provided for @leasesFailedToLoad.
  ///
  /// In en, this message translates to:
  /// **'Failed to load leases'**
  String get leasesFailedToLoad;

  /// No description provided for @leasesEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'No properties found'**
  String get leasesEmptyTitle;

  /// No description provided for @leasesEmptyHint.
  ///
  /// In en, this message translates to:
  /// **'You currently have no active leases'**
  String get leasesEmptyHint;

  /// No description provided for @forceUpdateTitle.
  ///
  /// In en, this message translates to:
  /// **'Update Required'**
  String get forceUpdateTitle;

  /// No description provided for @forceUpdateBody.
  ///
  /// In en, this message translates to:
  /// **'This version is no longer supported. Please update to the latest version to continue.'**
  String get forceUpdateBody;

  /// No description provided for @sessionRelogin.
  ///
  /// In en, this message translates to:
  /// **'Please log in again.'**
  String get sessionRelogin;

  /// No description provided for @invoiceDownloadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not download invoice (HTTP {code}).'**
  String invoiceDownloadFailed(String code);

  /// No description provided for @receiptDownloadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not download receipt (HTTP {code}).'**
  String receiptDownloadFailed(String code);

  /// No description provided for @pdfOpenFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not open PDF: {message}'**
  String pdfOpenFailed(String message);

  /// No description provided for @invoicePdfError.
  ///
  /// In en, this message translates to:
  /// **'Invoice PDF error: {error}'**
  String invoicePdfError(String error);

  /// No description provided for @receiptError.
  ///
  /// In en, this message translates to:
  /// **'Receipt error: {error}'**
  String receiptError(String error);

  /// No description provided for @labelCategory.
  ///
  /// In en, this message translates to:
  /// **'Category'**
  String get labelCategory;

  /// No description provided for @labelPriority.
  ///
  /// In en, this message translates to:
  /// **'Priority'**
  String get labelPriority;

  /// No description provided for @labelRequestDate.
  ///
  /// In en, this message translates to:
  /// **'Request Date'**
  String get labelRequestDate;

  /// No description provided for @labelCompletedDate.
  ///
  /// In en, this message translates to:
  /// **'Completed Date'**
  String get labelCompletedDate;

  /// No description provided for @labelAssignedTo.
  ///
  /// In en, this message translates to:
  /// **'Assigned To'**
  String get labelAssignedTo;

  /// No description provided for @labelDescription.
  ///
  /// In en, this message translates to:
  /// **'Description'**
  String get labelDescription;

  /// No description provided for @labelNotes.
  ///
  /// In en, this message translates to:
  /// **'Notes'**
  String get labelNotes;

  /// No description provided for @requestNumberShort.
  ///
  /// In en, this message translates to:
  /// **'Request #{id}'**
  String requestNumberShort(String id);

  /// No description provided for @requestedOnLabel.
  ///
  /// In en, this message translates to:
  /// **'Requested on {date}'**
  String requestedOnLabel(String date);

  /// No description provided for @labelUnitNumber.
  ///
  /// In en, this message translates to:
  /// **'Unit Number'**
  String get labelUnitNumber;

  /// No description provided for @labelMonthlyRent.
  ///
  /// In en, this message translates to:
  /// **'Monthly Rent'**
  String get labelMonthlyRent;

  /// No description provided for @labelSecurityDeposit.
  ///
  /// In en, this message translates to:
  /// **'Security Deposit'**
  String get labelSecurityDeposit;

  /// No description provided for @labelStartDate.
  ///
  /// In en, this message translates to:
  /// **'Start Date'**
  String get labelStartDate;

  /// No description provided for @labelEndDate.
  ///
  /// In en, this message translates to:
  /// **'End Date'**
  String get labelEndDate;

  /// No description provided for @labelDaysRemaining.
  ///
  /// In en, this message translates to:
  /// **'Days Remaining'**
  String get labelDaysRemaining;

  /// No description provided for @labelPaymentDueDay.
  ///
  /// In en, this message translates to:
  /// **'Payment Due Day'**
  String get labelPaymentDueDay;

  /// No description provided for @statusExpired.
  ///
  /// In en, this message translates to:
  /// **'Expired'**
  String get statusExpired;

  /// No description provided for @statusExpiringSoon.
  ///
  /// In en, this message translates to:
  /// **'Expiring Soon'**
  String get statusExpiringSoon;

  /// No description provided for @daysValue.
  ///
  /// In en, this message translates to:
  /// **'{days} days'**
  String daysValue(String days);

  /// No description provided for @paymentDueDayValue.
  ///
  /// In en, this message translates to:
  /// **'Day {day} of each month'**
  String paymentDueDayValue(String day);

  /// No description provided for @statusPending.
  ///
  /// In en, this message translates to:
  /// **'Pending'**
  String get statusPending;

  /// No description provided for @statusPaid.
  ///
  /// In en, this message translates to:
  /// **'Paid'**
  String get statusPaid;

  /// No description provided for @statusPartiallyPaid.
  ///
  /// In en, this message translates to:
  /// **'Partially Paid'**
  String get statusPartiallyPaid;

  /// No description provided for @statusOverdue.
  ///
  /// In en, this message translates to:
  /// **'Overdue'**
  String get statusOverdue;

  /// No description provided for @statusCancelled.
  ///
  /// In en, this message translates to:
  /// **'Cancelled'**
  String get statusCancelled;

  /// No description provided for @statusCompleted.
  ///
  /// In en, this message translates to:
  /// **'Completed'**
  String get statusCompleted;

  /// No description provided for @statusFailed.
  ///
  /// In en, this message translates to:
  /// **'Failed'**
  String get statusFailed;

  /// No description provided for @statusRefunded.
  ///
  /// In en, this message translates to:
  /// **'Refunded'**
  String get statusRefunded;

  /// No description provided for @statusActive.
  ///
  /// In en, this message translates to:
  /// **'Active'**
  String get statusActive;

  /// No description provided for @statusTerminated.
  ///
  /// In en, this message translates to:
  /// **'Terminated'**
  String get statusTerminated;

  /// No description provided for @statusInProgress.
  ///
  /// In en, this message translates to:
  /// **'In Progress'**
  String get statusInProgress;

  /// No description provided for @statusApproved.
  ///
  /// In en, this message translates to:
  /// **'Approved'**
  String get statusApproved;

  /// No description provided for @statusRejected.
  ///
  /// In en, this message translates to:
  /// **'Rejected'**
  String get statusRejected;

  /// No description provided for @statusOpen.
  ///
  /// In en, this message translates to:
  /// **'Open'**
  String get statusOpen;

  /// No description provided for @statusAssigned.
  ///
  /// In en, this message translates to:
  /// **'Assigned'**
  String get statusAssigned;

  /// No description provided for @payInvoicesInOrder.
  ///
  /// In en, this message translates to:
  /// **'Pay invoices in order. '**
  String get payInvoicesInOrder;

  /// No description provided for @startWithInvoiceHint.
  ///
  /// In en, this message translates to:
  /// **'Start with invoice #{id}. The remaining {count} will unlock once it\'s fully paid.'**
  String startWithInvoiceHint(String id, String count);

  /// No description provided for @payInvoiceFirst.
  ///
  /// In en, this message translates to:
  /// **'Pay invoice #{id} first'**
  String payInvoiceFirst(String id);

  /// No description provided for @legalCase.
  ///
  /// In en, this message translates to:
  /// **'Legal case'**
  String get legalCase;

  /// No description provided for @legalCaseNextHearing.
  ///
  /// In en, this message translates to:
  /// **'Next hearing: {date}'**
  String legalCaseNextHearing(String date);

  /// No description provided for @caseStatusOpen.
  ///
  /// In en, this message translates to:
  /// **'Open'**
  String get caseStatusOpen;

  /// No description provided for @caseStatusHearing.
  ///
  /// In en, this message translates to:
  /// **'Hearing'**
  String get caseStatusHearing;

  /// No description provided for @caseStatusWon.
  ///
  /// In en, this message translates to:
  /// **'Won'**
  String get caseStatusWon;

  /// No description provided for @caseStatusLost.
  ///
  /// In en, this message translates to:
  /// **'Lost'**
  String get caseStatusLost;

  /// No description provided for @caseStatusSettled.
  ///
  /// In en, this message translates to:
  /// **'Settled'**
  String get caseStatusSettled;

  /// No description provided for @caseStatusWithdrawn.
  ///
  /// In en, this message translates to:
  /// **'Withdrawn'**
  String get caseStatusWithdrawn;

  /// No description provided for @caseStatusClosed.
  ///
  /// In en, this message translates to:
  /// **'Closed'**
  String get caseStatusClosed;

  /// No description provided for @caseTypeEviction.
  ///
  /// In en, this message translates to:
  /// **'Eviction'**
  String get caseTypeEviction;

  /// No description provided for @caseTypeNonPayment.
  ///
  /// In en, this message translates to:
  /// **'Non-payment'**
  String get caseTypeNonPayment;

  /// No description provided for @caseTypeDamages.
  ///
  /// In en, this message translates to:
  /// **'Damages'**
  String get caseTypeDamages;

  /// No description provided for @caseTypeOther.
  ///
  /// In en, this message translates to:
  /// **'Other'**
  String get caseTypeOther;
}

class _AppLocalizationsDelegate extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) => <String>['ar', 'en'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {


  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'ar': return AppLocalizationsAr();
    case 'en': return AppLocalizationsEn();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.'
  );
}
