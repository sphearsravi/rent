import 'package:intl/intl.dart';
import '../constants/app_constants.dart';

/// Utility class for currency formatting operations
class CurrencyUtils {
  static final NumberFormat _currencyFormat = NumberFormat('#,##0.000', 'en_US');
  static final NumberFormat _compactFormat = NumberFormat.compact();

  /// Format amount as "1,234.567 KWD"
  static String formatAmount(double? amount, {bool showCurrency = true}) {
    if (amount == null) return showCurrency ? '0.000 ${AppConstants.currency}' : '0.000';
    final formatted = _currencyFormat.format(amount);
    return showCurrency ? '$formatted ${AppConstants.currency}' : formatted;
  }

  /// Format amount with currency prefix "KWD 1,234.567"
  static String formatAmountWithPrefix(double? amount) {
    if (amount == null) return '${AppConstants.currency} 0.000';
    return '${AppConstants.currency} ${_currencyFormat.format(amount)}';
  }

  /// Format compact amount (e.g., "1.2K KWD")
  static String formatCompact(double? amount, {bool showCurrency = true}) {
    if (amount == null) return showCurrency ? '0 ${AppConstants.currency}' : '0';
    final formatted = _compactFormat.format(amount);
    return showCurrency ? '$formatted ${AppConstants.currency}' : formatted;
  }

  /// Parse currency string to double
  static double? parseAmount(String? amountString) {
    if (amountString == null || amountString.isEmpty) return null;
    try {
      // Remove currency symbol and any non-numeric characters except decimal point
      final cleaned = amountString
          .replaceAll(AppConstants.currency, '')
          .replaceAll(',', '')
          .replaceAll(' ', '')
          .trim();
      return double.parse(cleaned);
    } catch (e) {
      return null;
    }
  }

  /// Convert amount to words (English)
  static String amountToWords(double amount) {
    if (amount == 0) return 'Zero';

    final dinars = amount.floor();
    final fils = ((amount - dinars) * 1000).round();

    String words = _numberToWords(dinars);
    words += dinars == 1 ? ' Dinar' : ' Dinars';

    if (fils > 0) {
      words += ' and ${_numberToWords(fils)}';
      words += fils == 1 ? ' Fil' : ' Fils';
    }

    return words;
  }

  static String _numberToWords(int number) {
    if (number == 0) return 'Zero';
    if (number < 0) return 'Minus ${_numberToWords(-number)}';

    final units = [
      '', 'One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven', 'Eight', 'Nine',
      'Ten', 'Eleven', 'Twelve', 'Thirteen', 'Fourteen', 'Fifteen', 'Sixteen',
      'Seventeen', 'Eighteen', 'Nineteen',
    ];
    final tens = [
      '', '', 'Twenty', 'Thirty', 'Forty', 'Fifty', 'Sixty', 'Seventy', 'Eighty', 'Ninety',
    ];

    String words = '';

    if (number >= 1000000) {
      words += '${_numberToWords(number ~/ 1000000)} Million ';
      number %= 1000000;
    }

    if (number >= 1000) {
      words += '${_numberToWords(number ~/ 1000)} Thousand ';
      number %= 1000;
    }

    if (number >= 100) {
      words += '${_numberToWords(number ~/ 100)} Hundred ';
      number %= 100;
    }

    if (number > 0) {
      if (words.isNotEmpty) words += 'and ';

      if (number < 20) {
        words += units[number];
      } else {
        words += tens[number ~/ 10];
        if (number % 10 > 0) {
          words += '-${units[number % 10]}';
        }
      }
    }

    return words.trim();
  }
}
