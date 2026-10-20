import 'package:flutter/widgets.dart';
import '../../l10n/app_localizations.dart';

/// Maps a raw backend status string (invoice / payment / lease / maintenance)
/// to a localized, user-facing label. Matching is case- and separator-
/// insensitive ("In Progress", "in_progress", "InProgress" all match).
/// Unknown statuses fall back to the original string so nothing disappears.
String localizeStatus(BuildContext context, String? status) {
  final l = AppLocalizations.of(context)!;
  final key = (status ?? '')
      .toLowerCase()
      .replaceAll(' ', '')
      .replaceAll('_', '')
      .replaceAll('-', '');
  switch (key) {
    case 'pending':
      return l.statusPending;
    case 'pendingverification':
      // Backend enum `PendingVerification` / invoice "Pending Verification".
      return l.statusPendingVerification;
    case 'paid':
      return l.statusPaid;
    case 'partiallypaid':
      return l.statusPartiallyPaid;
    case 'overdue':
      return l.statusOverdue;
    case 'cancelled':
    case 'canceled':
      return l.statusCancelled;
    case 'completed':
      return l.statusCompleted;
    case 'failed':
      return l.statusFailed;
    case 'refunded':
      return l.statusRefunded;
    case 'active':
      return l.statusActive;
    case 'expired':
      return l.statusExpired;
    case 'expiringsoon':
      return l.statusExpiringSoon;
    case 'terminated':
      return l.statusTerminated;
    case 'inprogress':
      return l.statusInProgress;
    case 'approved':
      return l.statusApproved;
    case 'rejected':
      return l.statusRejected;
    case 'open':
      return l.statusOpen;
    case 'assigned':
      return l.statusAssigned;
    default:
      return status ?? '';
  }
}

/// Localized label for a maintenance category. The English constant stays the
/// API value; only the displayed text is translated.
String localizeMaintenanceCategory(BuildContext context, String? category) {
  final l = AppLocalizations.of(context)!;
  switch ((category ?? '').toLowerCase()) {
    case 'plumbing':
      return l.maintCatPlumbing;
    case 'electrical':
      return l.maintCatElectrical;
    case 'hvac':
      return l.maintCatHvac;
    case 'appliance':
      return l.maintCatAppliance;
    case 'structural':
      return l.maintCatStructural;
    case 'other':
      return l.maintCatOther;
    default:
      return category ?? '';
  }
}

/// Localized label for a maintenance priority (Low/Medium/High/Emergency).
String localizeMaintenancePriority(BuildContext context, String? priority) {
  final l = AppLocalizations.of(context)!;
  switch ((priority ?? '').toLowerCase()) {
    case 'low':
      return l.maintPrioLow;
    case 'medium':
      return l.maintPrioMedium;
    case 'high':
      return l.maintPrioHigh;
    case 'emergency':
      return l.maintPrioEmergency;
    default:
      return priority ?? '';
  }
}

/// Localized label for a payment method/type (KNET / BankTransfer / Cash).
String localizePaymentType(BuildContext context, String? type) {
  final l = AppLocalizations.of(context)!;
  final key = (type ?? '').toLowerCase().replaceAll(' ', '').replaceAll('_', '');
  switch (key) {
    case 'knet':
      return 'KNET';
    case 'banktransfer':
      return l.paymentBankTransfer;
    case 'cash':
      return l.paymentCash;
    default:
      return type ?? '';
  }
}

/// Localized label for a legal-case status (Open/Hearing/Won/…).
String localizeCaseStatus(BuildContext context, String? status) {
  final l = AppLocalizations.of(context)!;
  switch ((status ?? '').toLowerCase()) {
    case 'open':
      return l.caseStatusOpen;
    case 'hearing':
      return l.caseStatusHearing;
    case 'won':
      return l.caseStatusWon;
    case 'lost':
      return l.caseStatusLost;
    case 'settled':
      return l.caseStatusSettled;
    case 'withdrawn':
      return l.caseStatusWithdrawn;
    case 'closed':
      return l.caseStatusClosed;
    default:
      return status ?? '';
  }
}

/// Localized label for a legal-case type (Eviction/NonPayment/Damages/Other).
String localizeCaseType(BuildContext context, String? type) {
  final l = AppLocalizations.of(context)!;
  final key = (type ?? '').toLowerCase().replaceAll('-', '').replaceAll('_', '');
  switch (key) {
    case 'eviction':
      return l.caseTypeEviction;
    case 'nonpayment':
      return l.caseTypeNonPayment;
    case 'damages':
      return l.caseTypeDamages;
    case 'other':
      return l.caseTypeOther;
    default:
      return type ?? '';
  }
}
