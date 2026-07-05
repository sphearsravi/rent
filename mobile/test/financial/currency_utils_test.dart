import 'package:flutter_test/flutter_test.dart';
import 'package:viijar_tenant/core/utils/currency_utils.dart';

void main() {
  group('CurrencyUtils.formatAmount', () {
    test('formats with 3 decimals and KWD suffix', () {
      expect(CurrencyUtils.formatAmount(1234.567), '1,234.567 KWD');
    });

    test('formats without currency when showCurrency is false', () {
      expect(
        CurrencyUtils.formatAmount(1234.567, showCurrency: false),
        '1,234.567',
      );
    });

    test('null formats as 0.000 KWD', () {
      expect(CurrencyUtils.formatAmount(null), '0.000 KWD');
    });

    test('null without currency formats as 0.000', () {
      expect(CurrencyUtils.formatAmount(null, showCurrency: false), '0.000');
    });
  });

  group('CurrencyUtils.formatAmountWithPrefix', () {
    test('formats with KWD prefix', () {
      expect(
        CurrencyUtils.formatAmountWithPrefix(1234.567),
        'KWD 1,234.567',
      );
    });

    test('null formats as KWD 0.000', () {
      expect(CurrencyUtils.formatAmountWithPrefix(null), 'KWD 0.000');
    });
  });

  group('CurrencyUtils.parseAmount', () {
    test('round-trips a formatted amount', () {
      final parsed = CurrencyUtils.parseAmount('1,234.567 KWD');
      expect(parsed, 1234.567);
    });

    test('parse then format is stable', () {
      final formatted = CurrencyUtils.formatAmount(1234.567);
      final parsed = CurrencyUtils.parseAmount(formatted);
      expect(parsed, 1234.567);
    });

    test('null returns null', () {
      expect(CurrencyUtils.parseAmount(null), isNull);
    });

    test('empty string returns null', () {
      expect(CurrencyUtils.parseAmount(''), isNull);
    });
  });

  group('CurrencyUtils fils precision (3 decimals)', () {
    test('0.001', () {
      expect(CurrencyUtils.formatAmount(0.001), '0.001 KWD');
    });

    test('0.999', () {
      expect(CurrencyUtils.formatAmount(0.999), '0.999 KWD');
    });

    test('1000.000', () {
      expect(CurrencyUtils.formatAmount(1000.000), '1,000.000 KWD');
    });
  });

  group('CurrencyUtils.amountToWords', () {
    test('1.000 contains One Dinar', () {
      expect(CurrencyUtils.amountToWords(1.000), contains('One Dinar'));
    });

    test('0 is Zero', () {
      expect(CurrencyUtils.amountToWords(0), 'Zero');
    });

    test('1.500 contains Dinar and Fils', () {
      final words = CurrencyUtils.amountToWords(1.500);
      expect(words, contains('One Dinar'));
      expect(words, contains('Fil'));
    });
  });
}
