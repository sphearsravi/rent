import 'package:flutter_test/flutter_test.dart';
import 'package:viijar_tenant/presentation/viewmodels/invoice_viewmodel.dart';

void main() {
  group('InvoiceFilter.apiValue', () {
    test('all is null', () {
      expect(InvoiceFilter.all.apiValue, isNull);
    });

    test('outstanding is Outstanding', () {
      expect(InvoiceFilter.outstanding.apiValue, 'Outstanding');
    });

    test('overdue is Overdue', () {
      expect(InvoiceFilter.overdue.apiValue, 'Overdue');
    });

    test('paid is Paid', () {
      expect(InvoiceFilter.paid.apiValue, 'Paid');
    });
  });

  group('InvoiceFilter.label', () {
    test('every filter has a non-empty label', () {
      for (final filter in InvoiceFilter.values) {
        expect(filter.label, isNotEmpty,
            reason: '${filter.name} should have a non-empty label');
      }
    });
  });
}
