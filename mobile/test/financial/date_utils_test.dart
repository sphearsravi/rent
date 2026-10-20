import 'package:flutter_test/flutter_test.dart';
import 'package:intl/intl.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:viijar_tenant/core/utils/date_utils.dart';

void main() {
  setUpAll(() async {
    await initializeDateFormatting();
  });

  group('AppDateUtils.formatShortDate (English)', () {
    setUp(() {
      Intl.defaultLocale = 'en';
    });

    tearDown(() {
      Intl.defaultLocale = 'en';
    });

    test('formats dd MMM yyyy', () {
      expect(AppDateUtils.formatShortDate(DateTime(2026, 7, 5)), '05 Jul 2026');
    });

    test('null returns dash', () {
      expect(AppDateUtils.formatShortDate(null), '-');
    });
  });

  group('AppDateUtils.formatShortDate (Arabic — Western digits)', () {
    setUp(() {
      Intl.defaultLocale = 'ar';
    });

    tearDown(() {
      Intl.defaultLocale = 'en';
    });

    test('uses Western digits and Arabic month name', () {
      final result = AppDateUtils.formatShortDate(DateTime(2026, 7, 5));

      // Contains Western digits.
      expect(result, matches(RegExp(r'\d')));
      expect(result, contains('05'));
      expect(result, contains('2026'));

      // Contains NO Arabic-Indic digits.
      const arabicIndic = ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩'];
      for (final d in arabicIndic) {
        expect(result.contains(d), isFalse,
            reason: 'result "$result" must not contain Arabic-Indic digit $d',);
      }

      // Month name should be localized (Arabic), i.e. contains Arabic letters.
      expect(result, matches(RegExp(r'[؀-ۿ]')));
    });
  });

  group('AppDateUtils.formatIsoDate', () {
    setUp(() {
      Intl.defaultLocale = 'en';
    });

    tearDown(() {
      Intl.defaultLocale = 'en';
    });

    test('formats yyyy-MM-dd', () {
      expect(AppDateUtils.formatIsoDate(DateTime(2026, 7, 5)), '2026-07-05');
    });

    test('null returns empty string', () {
      expect(AppDateUtils.formatIsoDate(null), '');
    });
  });
}
