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
