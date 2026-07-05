import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:viijar_tenant/data/models/invoice_detail_model.dart';
import 'package:viijar_tenant/data/models/invoice_list_result.dart';
import 'package:viijar_tenant/data/models/payment_model.dart' show InvoiceModel;
import 'package:viijar_tenant/data/services/tenant_api_service.dart';
import 'package:viijar_tenant/presentation/viewmodels/invoice_viewmodel.dart';

class MockTenantApiService extends Mock implements TenantApiService {}

/// Build an [InvoiceModel] with the fields the view-model actually reads
/// (`status` for `isPaid`, `balanceAmount` for `balance`).
InvoiceModel makeInvoice({
  required int id,
  required String status,
  double totalAmount = 100.0,
  double? balanceAmount,
}) {
  return InvoiceModel(
    invoiceId: id,
    totalAmount: totalAmount,
    balanceAmount: balanceAmount,
    dueDate: DateTime(2026, 1, 1),
    status: status,
  );
}

InvoiceListResult makeResult({
  required int totalCount,
  int pageNumber = 1,
  int pageSize = 20,
  required List<InvoiceModel> invoices,
}) {
  return InvoiceListResult(
    totalCount: totalCount,
    pageNumber: pageNumber,
    pageSize: pageSize,
    invoices: invoices,
  );
}

InvoiceDetailModel makeDetail(int id) {
  return InvoiceDetailModel(
    invoiceId: id,
    totalAmount: 500.0,
    paidAmount: 100.0,
    balanceAmount: 400.0,
    invoiceDate: DateTime(2026, 1, 1),
    dueDate: DateTime(2026, 2, 1),
    status: 'Pending',
  );
}

void main() {
  late MockTenantApiService api;
  late InvoiceViewModel vm;

  setUp(() {
    api = MockTenantApiService();
    vm = InvoiceViewModel(api);
  });

  // `getInvoices(int tenantId, {String? status, int pageNumber, int pageSize})`
  // — tenantId is POSITIONAL, the rest are named.
  void stubGetInvoices(InvoiceListResult result) {
    when(() => api.getInvoices(
          any(),
          status: any(named: 'status'),
          pageNumber: any(named: 'pageNumber'),
          pageSize: any(named: 'pageSize'),
        )).thenAnswer((_) async => result);
  }

  group('refresh', () {
    test('success populates invoices, totalCount, and loaded state', () async {
      final inv1 = makeInvoice(id: 1, status: 'Pending');
      final inv2 = makeInvoice(id: 2, status: 'Paid');
      stubGetInvoices(makeResult(totalCount: 2, invoices: [inv1, inv2]));

      await vm.refresh(711);

      expect(vm.invoices.length, 2);
      expect(vm.totalCount, 2);
      expect(vm.state, InvoiceState.loaded);
      expect(vm.errorMessage, isNull);
      expect(vm.isLoading, isFalse);
    });

    test('error sets errorMessage, leaves invoices empty, error state',
        () async {
      when(() => api.getInvoices(
            any(),
            status: any(named: 'status'),
            pageNumber: any(named: 'pageNumber'),
            pageSize: any(named: 'pageSize'),
          )).thenThrow(Exception('network down'));

      await vm.refresh(711);

      expect(vm.errorMessage, isNotNull);
      expect(vm.invoices, isEmpty);
      expect(vm.state, InvoiceState.error);
    });
  });

  group('setFilter', () {
    test('changes filter and reloads with mapped status', () async {
      stubGetInvoices(
          makeResult(totalCount: 1, invoices: [makeInvoice(id: 1, status: 'Paid')]));

      await vm.setFilter(711, InvoiceFilter.paid);

      expect(vm.filter, InvoiceFilter.paid);
      verify(() => api.getInvoices(
            711,
            status: 'Paid',
            pageNumber: any(named: 'pageNumber'),
            pageSize: any(named: 'pageSize'),
          )).called(1);
    });
  });

  group('loadMore', () {
    test('appends the next page and updates hasMore', () async {
      final firstPage =
          List.generate(20, (i) => makeInvoice(id: i, status: 'Pending'));
      final secondPage =
          List.generate(20, (i) => makeInvoice(id: 100 + i, status: 'Pending'));

      // First refresh: 40 total, 20 loaded -> hasMore true.
      stubGetInvoices(makeResult(totalCount: 40, invoices: firstPage));
      await vm.refresh(711);
      expect(vm.invoices.length, 20);
      expect(vm.hasMore, isTrue);

      // Next page returns 20 more.
      stubGetInvoices(
          makeResult(totalCount: 40, pageNumber: 2, invoices: secondPage));
      await vm.loadMore(711);

      expect(vm.invoices.length, 40);
      expect(vm.hasMore, isFalse);
      expect(vm.state, InvoiceState.loaded);
    });

    test('hasMore is false when all invoices are loaded', () async {
      final page =
          List.generate(3, (i) => makeInvoice(id: i, status: 'Pending'));
      stubGetInvoices(makeResult(totalCount: 3, invoices: page));

      await vm.refresh(711);

      expect(vm.invoices.length, 3);
      expect(vm.hasMore, isFalse);
    });
  });

  group('outstandingBalance', () {
    test('sums balances of unpaid invoices only', () async {
      // outstandingBalance = sum of `balance` for invoices where !isPaid.
      final paid = makeInvoice(id: 1, status: 'Paid', balanceAmount: 0.0);
      final pending =
          makeInvoice(id: 2, status: 'Pending', balanceAmount: 150.0);
      final overdue =
          makeInvoice(id: 3, status: 'Overdue', balanceAmount: 250.0);
      stubGetInvoices(
          makeResult(totalCount: 3, invoices: [paid, pending, overdue]));

      await vm.refresh(711);

      // 150 + 250 = 400 (paid excluded).
      expect(vm.outstandingBalance, 400.0);
    });
  });

  group('loadInvoiceDetails', () {
    test('sets selectedInvoice on success', () async {
      when(() => api.getInvoiceDetails(any(), any()))
          .thenAnswer((_) async => makeDetail(5));

      await vm.loadInvoiceDetails(711, 5);

      expect(vm.selectedInvoice, isNotNull);
      expect(vm.selectedInvoice!.invoiceId, 5);
      expect(vm.state, InvoiceState.loaded);
    });
  });
}
