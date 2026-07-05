import 'package:intl/intl.dart';

/// Utility class for date formatting operations.
///
/// Formatters are built per-call against the app's active locale
/// (`Intl.defaultLocale`, set by LocaleProvider) so month/day names render in
/// Arabic when the UI is Arabic. Digits are always normalised to Western
/// 0-9 via [_toWesternDigits] — the app's deliberate choice is Latin digits
/// everywhere (money and dates) for financial clarity, even in Arabic.
class AppDateUtils {
  /// The active app locale, or 'en' when following the device default.
  static String get _locale => Intl.defaultLocale ?? 'en';

  /// Map Arabic-Indic digits (٠-٩) back to Western 0-9. Arabic `DateFormat`
  /// emits Arabic-Indic numerals; we keep month/day NAMES localized but the
  /// numerals Western.
  static String _toWesternDigits(String input) {
    const arabicIndic = ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩'];
    var out = input;
    for (var i = 0; i < 10; i++) {
      out = out.replaceAll(arabicIndic[i], i.toString());
    }
    return out;
  }

  /// Format date as "dd MMM yyyy" (e.g., "15 Nov 2024" / "15 نوفمبر 2024")
  static String formatShortDate(DateTime? date) {
    if (date == null) return '-';
    return _toWesternDigits(DateFormat('dd MMM yyyy', _locale).format(date));
  }

  /// Format date as "EEEE, dd MMMM yyyy" (e.g., "Friday, 15 November 2024")
  static String formatLongDate(DateTime? date) {
    if (date == null) return '-';
    return _toWesternDigits(
        DateFormat('EEEE, dd MMMM yyyy', _locale).format(date));
  }

  /// Format date as "MMM yyyy" (e.g., "Nov 2024")
  static String formatMonthYear(DateTime? date) {
    if (date == null) return '-';
    return _toWesternDigits(DateFormat('MMM yyyy', _locale).format(date));
  }

  /// Format time as "hh:mm a" (e.g., "02:30 PM")
  static String formatTime(DateTime? date) {
    if (date == null) return '-';
    return _toWesternDigits(DateFormat('hh:mm a', _locale).format(date));
  }

  /// Format date and time as "dd MMM yyyy, hh:mm a" (e.g., "15 Nov 2024, 02:30 PM")
  static String formatDateTime(DateTime? date) {
    if (date == null) return '-';
    return _toWesternDigits(
        DateFormat('dd MMM yyyy, hh:mm a', _locale).format(date));
  }

  /// Format date as ISO format "yyyy-MM-dd" (e.g., "2024-11-15").
  /// Always locale-neutral Western digits (used for machine/tokens).
  static String formatIsoDate(DateTime? date) {
    if (date == null) return '';
    return DateFormat('yyyy-MM-dd').format(date);
  }

  /// Parse ISO date string to DateTime
  static DateTime? parseIsoDate(String? dateString) {
    if (dateString == null || dateString.isEmpty) return null;
    try {
      return DateTime.parse(dateString);
    } catch (e) {
      return null;
    }
  }

  /// Get relative time string (e.g., "2 days ago", "in 3 days")
  static String getRelativeTime(DateTime? date) {
    if (date == null) return '-';

    final now = DateTime.now();
    final difference = date.difference(now);

    if (difference.isNegative) {
      // Past dates
      final absDifference = difference.abs();
      if (absDifference.inDays > 365) {
        final years = (absDifference.inDays / 365).floor();
        return '$years year${years > 1 ? 's' : ''} ago';
      } else if (absDifference.inDays > 30) {
        final months = (absDifference.inDays / 30).floor();
        return '$months month${months > 1 ? 's' : ''} ago';
      } else if (absDifference.inDays > 0) {
        return '${absDifference.inDays} day${absDifference.inDays > 1 ? 's' : ''} ago';
      } else if (absDifference.inHours > 0) {
        return '${absDifference.inHours} hour${absDifference.inHours > 1 ? 's' : ''} ago';
      } else if (absDifference.inMinutes > 0) {
        return '${absDifference.inMinutes} minute${absDifference.inMinutes > 1 ? 's' : ''} ago';
      } else {
        return 'Just now';
      }
    } else {
      // Future dates
      if (difference.inDays > 365) {
        final years = (difference.inDays / 365).floor();
        return 'in $years year${years > 1 ? 's' : ''}';
      } else if (difference.inDays > 30) {
        final months = (difference.inDays / 30).floor();
        return 'in $months month${months > 1 ? 's' : ''}';
      } else if (difference.inDays > 0) {
        return 'in ${difference.inDays} day${difference.inDays > 1 ? 's' : ''}';
      } else if (difference.inHours > 0) {
        return 'in ${difference.inHours} hour${difference.inHours > 1 ? 's' : ''}';
      } else if (difference.inMinutes > 0) {
        return 'in ${difference.inMinutes} minute${difference.inMinutes > 1 ? 's' : ''}';
      } else {
        return 'Now';
      }
    }
  }

  /// Calculate days remaining until a date
  static int getDaysRemaining(DateTime? endDate) {
    if (endDate == null) return 0;
    return endDate.difference(DateTime.now()).inDays;
  }

  /// Check if a date is overdue
  static bool isOverdue(DateTime? dueDate) {
    if (dueDate == null) return false;
    return dueDate.isBefore(DateTime.now());
  }

  /// Check if a date is within the next N days
  static bool isWithinDays(DateTime? date, int days) {
    if (date == null) return false;
    final now = DateTime.now();
    final futureDate = now.add(Duration(days: days));
    return date.isAfter(now) && date.isBefore(futureDate);
  }

  /// Get the first day of the current month
  static DateTime getFirstDayOfMonth() {
    final now = DateTime.now();
    return DateTime(now.year, now.month, 1);
  }

  /// Get the last day of the current month
  static DateTime getLastDayOfMonth() {
    final now = DateTime.now();
    return DateTime(now.year, now.month + 1, 0);
  }
}
