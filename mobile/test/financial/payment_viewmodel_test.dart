import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:viijar_tenant/data/models/payment_model.dart';
import 'package:viijar_tenant/data/services/api_service.dart';
import 'package:viijar_tenant/data/services/tenant_api_service.dart';
import 'package:viijar_tenant/presentation/viewmodels/payment_viewmodel.dart';

class MockTenantApiService extends Mock implements TenantApiService {}

/// Builds an [InvoiceModel] with sensible defaults for tests.
InvoiceModel buildInvoice({
  int invoiceId = 1001,
  int? leaseId = 501,
  int? propertyId = 301,
  double totalAmount = 100.0,
  String status = 'Pending',
}) {
  return InvoiceModel(
    invoiceId: invoiceId,
    leaseId: leaseId,
    tenantId: 711,
    propertyId: propertyId,
    totalAmount: totalAmount,
    dueDate: DateTime(2026),
    status: status,
  );
}

/// Builds a [PaymentModel] with sensible defaults for tests.
PaymentModel buildPayment({
  int paymentId = 1,
  double amountPaid = 50.0,
  String paymentStatus = 'Completed',
}) {
  return PaymentModel(
    paymentId: paymentId,
    amountPaid: amountPaid,
    paymentDate: DateTime(2026),
    paymentMethod: 'KNET',
    paymentStatus: paymentStatus,
  );
}

void main() {
  setUpAll(() {
    // Needed because tests stub `initiateKNETPayment(any())`.
    registerFallbackValue(
      KnetPaymentRequest(
        invoiceId: 0,
        tenantId: 0,
        propertyId: 0,
        leaseId: 0,
        amount: 0.0,
      ),
    );
  });

  late MockTenantApiService mockApi;
  late PaymentViewModel vm;

  setUp(() {
    mockApi = MockTenantApiService();
    vm = PaymentViewModel(mockApi);
  });

  group('loadPendingInvoices', () {
    test('populates pendingInvoices on success', () async {
      when(() => mockApi.getPendingInvoices(711)).thenAnswer(
        (_) async => [
          buildInvoice(),
          buildInvoice(invoiceId: 1002),
        ],
      );

      vm.setTenantId(711);
      await vm.loadPendingInvoices();

      expect(vm.pendingInvoices, hasLength(2));
      expect(vm.error, isNull);
      verify(() => mockApi.getPendingInvoices(711)).called(1);
    });

    test('sets error and leaves invoices empty on ApiException', () async {
      when(() => mockApi.getPendingInvoices(711)).thenThrow(
        ApiException('Server error', kind: ApiErrorKind.serverError),
      );

      vm.setTenantId(711);
      await vm.loadPendingInvoices();

      expect(vm.error, isNotNull);
      expect(vm.pendingInvoices, isEmpty);
      expect(vm.state, PaymentState.error);
    });

    test('sets error when tenant id was never set', () async {
      await vm.loadPendingInvoices();

      expect(vm.error, 'Tenant ID not set');
      expect(vm.pendingInvoices, isEmpty);
    });
  });

  group('initiateKNETPayment', () {
    test('returns response with redirectUrl on success', () async {
      final inv = buildInvoice();
      when(() => mockApi.initiateKNETPayment(any())).thenAnswer(
        (_) async => KnetPaymentResponse(
          success: true,
          redirectUrl: 'https://kpay.com.kw/pay/abc',
          paymentId: 'PMT1',
        ),
      );

      final response = await vm.initiateKNETPayment(invoice: inv, tenantId: 711);

      expect(response, isNotNull);
      expect(response!.redirectUrl, 'https://kpay.com.kw/pay/abc');
      expect(vm.isPaymentBlocked, isFalse);
      expect(vm.state, PaymentState.loaded);
    });

    test('sets blocked fields on 409 paymentBlocked', () async {
      final inv = buildInvoice();
      when(() => mockApi.initiateKNETPayment(any())).thenThrow(
        ApiException(
          'Pay your oldest invoice first',
          kind: ApiErrorKind.paymentBlocked,
          oldestInvoiceId: 1401,
          oldestBalance: 50.0,
        ),
      );

      final response = await vm.initiateKNETPayment(invoice: inv, tenantId: 711);

      expect(response, isNull);
      expect(vm.isPaymentBlocked, isTrue);
      expect(vm.blockedByInvoiceId, 1401);
      expect(vm.blockedByBalance, 50.0);
      expect(vm.state, PaymentState.error);
    });
  });

  group('submitBankTransfer', () {
    test('returns true on success', () async {
      when(() => mockApi.submitBankTransfer(
            tenantId: any(named: 'tenantId'),
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            bankName: any(named: 'bankName'),
            transferDate: any(named: 'transferDate'),
            proofFilePath: any(named: 'proofFilePath'),
            referenceNumber: any(named: 'referenceNumber'),
            notes: any(named: 'notes'),
          ),).thenAnswer((_) async => true);

      vm.setTenantId(711);
      final result = await vm.submitBankTransfer(
        invoiceId: 1001,
        amount: 100.0,
        bankName: 'NBK',
        transferDate: DateTime(2026),
        proofFilePath: '/tmp/proof.jpg',
      );

      expect(result, isTrue);
      expect(vm.state, PaymentState.submitted);
    });

    test('returns false and sets blocked fields on paymentBlocked', () async {
      when(() => mockApi.submitBankTransfer(
            tenantId: any(named: 'tenantId'),
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            bankName: any(named: 'bankName'),
            transferDate: any(named: 'transferDate'),
            proofFilePath: any(named: 'proofFilePath'),
            referenceNumber: any(named: 'referenceNumber'),
            notes: any(named: 'notes'),
          ),).thenThrow(
        ApiException(
          'Pay your oldest invoice first',
          kind: ApiErrorKind.paymentBlocked,
          oldestInvoiceId: 1401,
          oldestBalance: 75.0,
        ),
      );

      vm.setTenantId(711);
      final result = await vm.submitBankTransfer(
        invoiceId: 1001,
        amount: 100.0,
        bankName: 'NBK',
        transferDate: DateTime(2026),
        proofFilePath: '/tmp/proof.jpg',
      );

      expect(result, isFalse);
      expect(vm.isPaymentBlocked, isTrue);
      expect(vm.blockedByInvoiceId, 1401);
      expect(vm.blockedByBalance, 75.0);
    });

    test('returns false and sets error on generic ApiException', () async {
      when(() => mockApi.submitBankTransfer(
            tenantId: any(named: 'tenantId'),
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            bankName: any(named: 'bankName'),
            transferDate: any(named: 'transferDate'),
            proofFilePath: any(named: 'proofFilePath'),
            referenceNumber: any(named: 'referenceNumber'),
            notes: any(named: 'notes'),
          ),).thenThrow(
        ApiException('Server error', kind: ApiErrorKind.serverError),
      );

      vm.setTenantId(711);
      final result = await vm.submitBankTransfer(
        invoiceId: 1001,
        amount: 100.0,
        bankName: 'NBK',
        transferDate: DateTime(2026),
        proofFilePath: '/tmp/proof.jpg',
      );

      expect(result, isFalse);
      expect(vm.error, isNotNull);
      expect(vm.isPaymentBlocked, isFalse);
    });

    // Regression: the invoice-driven "Pay now" path never called
    // setTenantId(), so every bank transfer failed client-side with
    // "Tenant ID not set". The server derives the tenant from the JWT,
    // so the call must go through regardless.
    test('still submits when tenant id was never set', () async {
      when(() => mockApi.submitBankTransfer(
            tenantId: any(named: 'tenantId'),
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            bankName: any(named: 'bankName'),
            transferDate: any(named: 'transferDate'),
            proofFilePath: any(named: 'proofFilePath'),
            referenceNumber: any(named: 'referenceNumber'),
            notes: any(named: 'notes'),
          ),).thenAnswer((_) async => true);

      final result = await vm.submitBankTransfer(
        invoiceId: 1001,
        amount: 100.0,
        bankName: 'NBK',
        transferDate: DateTime(2026),
        proofFilePath: '/tmp/proof.jpg',
      );

      expect(result, isTrue);
      expect(vm.error, isNull);
      verify(() => mockApi.submitBankTransfer(
            tenantId: 0,
            invoiceId: 1001,
            amount: 100.0,
            bankName: 'NBK',
            transferDate: DateTime(2026),
            proofFilePath: '/tmp/proof.jpg',
          ),).called(1);
    });

    test('explicit tenantId argument wins over the stored one', () async {
      when(() => mockApi.submitBankTransfer(
            tenantId: any(named: 'tenantId'),
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            bankName: any(named: 'bankName'),
            transferDate: any(named: 'transferDate'),
            proofFilePath: any(named: 'proofFilePath'),
            referenceNumber: any(named: 'referenceNumber'),
            notes: any(named: 'notes'),
          ),).thenAnswer((_) async => true);

      vm.setTenantId(1);
      await vm.submitBankTransfer(
        invoiceId: 1001,
        amount: 100.0,
        bankName: 'NBK',
        transferDate: DateTime(2026),
        proofFilePath: '/tmp/proof.jpg',
        tenantId: 711,
      );

      verify(() => mockApi.submitBankTransfer(
            tenantId: 711,
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            bankName: any(named: 'bankName'),
            transferDate: any(named: 'transferDate'),
            proofFilePath: any(named: 'proofFilePath'),
            referenceNumber: any(named: 'referenceNumber'),
            notes: any(named: 'notes'),
          ),).called(1);
    });
  });

  group('requestCashPayment', () {
    void stubCash({bool ok = true}) {
      when(() => mockApi.requestCashPayment(
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            notes: any(named: 'notes'),
          ),).thenAnswer((_) async => ok);
    }

    test('full balance → no amount is sent (server computes it)', () async {
      stubCash();

      final result = await vm.requestCashPayment(invoiceId: 1001);

      expect(result, isTrue);
      expect(vm.state, PaymentState.submitted);
      verify(() => mockApi.requestCashPayment(invoiceId: 1001)).called(1);
    });

    test('partial amount and notes are forwarded verbatim', () async {
      stubCash();

      await vm.requestCashPayment(
        invoiceId: 1001,
        amount: 100.5,
        notes: 'rest next week',
      );

      verify(() => mockApi.requestCashPayment(
            invoiceId: 1001,
            amount: 100.5,
            notes: 'rest next week',
          ),).called(1);
    });

    test('409 pay-oldest-first surfaces the blocking invoice', () async {
      when(() => mockApi.requestCashPayment(
            invoiceId: any(named: 'invoiceId'),
            amount: any(named: 'amount'),
            notes: any(named: 'notes'),
          ),).thenThrow(ApiException(
        'Pay invoice #1000 first',
        kind: ApiErrorKind.paymentBlocked,
        oldestInvoiceId: 1000,
        oldestBalance: 250,
      ),);

      final result = await vm.requestCashPayment(invoiceId: 1001, amount: 50);

      expect(result, isFalse);
      expect(vm.isPaymentBlocked, isTrue);
      expect(vm.blockedByInvoiceId, 1000);
    });
  });

  group('loadPayments pagination', () {
    test('hasMorePages true when a full page of 20 is returned', () async {
      final full = List.generate(20, (i) => buildPayment(paymentId: i + 1));
      when(() => mockApi.getPayments(711,
              pageNumber: any(named: 'pageNumber'),
              pageSize: any(named: 'pageSize'),),)
          .thenAnswer((_) async => full);

      await vm.loadPayments(711);

      expect(vm.payments, hasLength(20));
      expect(vm.hasMorePages, isTrue);
    });

    test('hasMorePages false when fewer than 20 returned', () async {
      final partial = List.generate(5, (i) => buildPayment(paymentId: i + 1));
      when(() => mockApi.getPayments(711,
              pageNumber: any(named: 'pageNumber'),
              pageSize: any(named: 'pageSize'),),)
          .thenAnswer((_) async => partial);

      await vm.loadPayments(711);

      expect(vm.payments, hasLength(5));
      expect(vm.hasMorePages, isFalse);
    });
  });

  group('computed getters', () {
    test('completedPayments and totalPaidAmount reflect mixed statuses',
        () async {
      final mixed = [
        buildPayment(amountPaid: 100.0),
        buildPayment(paymentId: 2, paymentStatus: 'Approved'),
        buildPayment(paymentId: 3, amountPaid: 30.0, paymentStatus: 'Pending'),
        buildPayment(paymentId: 4, amountPaid: 20.0, paymentStatus: 'Rejected'),
      ];
      when(() => mockApi.getPayments(711,
              pageNumber: any(named: 'pageNumber'),
              pageSize: any(named: 'pageSize'),),)
          .thenAnswer((_) async => mixed);

      await vm.loadPayments(711);

      // Completed + Approved both count as completed.
      expect(vm.completedPayments, hasLength(2));
      expect(vm.totalPaidAmount, 150.0);
    });
  });
}
