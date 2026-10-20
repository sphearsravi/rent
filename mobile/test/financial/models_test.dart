import 'package:flutter_test/flutter_test.dart';
import 'package:viijar_tenant/data/models/payment_model.dart';
import 'package:viijar_tenant/data/models/invoice_detail_model.dart';

InvoiceModel _invoice({
  required String status,
  double totalAmount = 100.0,
  double? paidAmount,
  double? balanceAmount,
}) {
  return InvoiceModel(
    invoiceId: 1,
    totalAmount: totalAmount,
    paidAmount: paidAmount,
    balanceAmount: balanceAmount,
    dueDate: DateTime(2026, 7, 5),
    status: status,
  );
}

PaymentModel _payment(String status) {
  return PaymentModel(
    paymentId: 1,
    amountPaid: 50.0,
    paymentDate: DateTime(2026, 7, 5),
    paymentMethod: 'KNET',
    paymentStatus: status,
  );
}

InvoiceDetailModel _detail(String status) {
  return InvoiceDetailModel(
    invoiceId: 1,
    totalAmount: 100.0,
    paidAmount: 40.0,
    balanceAmount: 60.0,
    invoiceDate: DateTime(2026, 7),
    dueDate: DateTime(2026, 7, 5),
    status: status,
  );
}

void main() {
  group('InvoiceModel status getters', () {
    test('isPaid true for Paid', () {
      expect(_invoice(status: 'Paid').isPaid, isTrue);
    });

    test('isOverdue true for Overdue', () {
      expect(_invoice(status: 'Overdue').isOverdue, isTrue);
    });

    test('isPending true for Pending', () {
      expect(_invoice(status: 'Pending').isPending, isTrue);
    });

    test('getters are mutually exclusive for a given status', () {
      final paid = _invoice(status: 'Paid');
      expect(paid.isOverdue, isFalse);
      expect(paid.isPending, isFalse);
    });
  });

  group('InvoiceModel.balance', () {
    test('uses balanceAmount when present', () {
      final inv = _invoice(
        status: 'Pending',
        paidAmount: 30.0,
        balanceAmount: 65.0,
      );
      expect(inv.balance, 65.0);
    });

    test('computes totalAmount - paidAmount when balanceAmount null', () {
      final inv = _invoice(
        status: 'Pending',
        paidAmount: 30.0,
      );
      expect(inv.balance, 70.0);
    });

    test('treats null paidAmount as 0', () {
      final inv = _invoice(
        status: 'Pending',
      );
      expect(inv.balance, 100.0);
    });
  });

  group('PaymentModel status getters', () {
    test('isCompleted true for Completed', () {
      expect(_payment('Completed').isCompleted, isTrue);
    });

    test('isCompleted true for Approved', () {
      expect(_payment('Approved').isCompleted, isTrue);
    });

    test('isRejected true for Rejected', () {
      expect(_payment('Rejected').isRejected, isTrue);
    });

    test('isRejected true for Failed', () {
      expect(_payment('Failed').isRejected, isTrue);
    });

    test('isPending true for Pending', () {
      expect(_payment('Pending').isPending, isTrue);
    });

    test('Pending is neither completed nor rejected', () {
      final p = _payment('Pending');
      expect(p.isCompleted, isFalse);
      expect(p.isRejected, isFalse);
    });
  });

  group('InvoiceDetailModel status getters', () {
    test('isPaid true for Paid', () {
      expect(_detail('Paid').isPaid, isTrue);
    });

    test('isPartiallyPaid true for "Partially Paid"', () {
      expect(_detail('Partially Paid').isPartiallyPaid, isTrue);
    });

    test('isOutstanding true for Pending', () {
      expect(_detail('Pending').isOutstanding, isTrue);
    });

    test('isOutstanding true for Overdue', () {
      expect(_detail('Overdue').isOutstanding, isTrue);
    });

    test('isOutstanding true for Partially Paid', () {
      expect(_detail('Partially Paid').isOutstanding, isTrue);
    });

    test('isOutstanding false for Paid', () {
      expect(_detail('Paid').isOutstanding, isFalse);
    });
  });

  group('InvoicePaymentSummary.isCompleted', () {
    InvoicePaymentSummary summary(String status) => InvoicePaymentSummary(
          paymentId: 1,
          paymentDate: DateTime(2026, 7, 5),
          amountPaid: 50.0,
          paymentType: 'KNET',
          status: status,
        );

    test('true for Completed', () {
      expect(summary('Completed').isCompleted, isTrue);
    });

    test('false for Pending', () {
      expect(summary('Pending').isCompleted, isFalse);
    });
  });
}
