import 'package:flutter/foundation.dart';
import '../../data/models/invoice_detail_model.dart';
import '../../data/models/invoice_list_result.dart';
import '../../data/models/payment_model.dart' show InvoiceModel;
import '../../data/services/tenant_api_service.dart';

enum InvoiceState { initial, loading, loaded, loadingMore, error }

/// Server-side filter values. Map directly to the `status` query param the
/// backend understands. "Outstanding" is a UI convenience that the backend
/// folds into Pending + Overdue.
enum InvoiceFilter {
  all('All', null),
  outstanding('Outstanding', 'Outstanding'),
  paid('Paid', 'Paid'),
  overdue('Overdue', 'Overdue');

  const InvoiceFilter(this.label, this.apiValue);
  final String label;
  final String? apiValue;
}

/// View-model for the Invoices tab.
///
/// Owns:
/// - the paginated list of [InvoiceModel] for the active filter
/// - the currently selected filter
/// - the selected invoice's [InvoiceDetailModel] (for the detail screen)
///
/// Pagination strategy: fetch the first page on filter change / refresh,
/// then `loadMore()` appends successive pages until [InvoiceListResult.hasMore]
/// is false.
class InvoiceViewModel extends ChangeNotifier {
  InvoiceViewModel(this._apiService);

  final TenantApiService _apiService;

  InvoiceState _state = InvoiceState.initial;
  String? _errorMessage;
  InvoiceFilter _filter = InvoiceFilter.all;
  final List<InvoiceModel> _invoices = [];
  int _pageNumber = 1;
  int _totalCount = 0;
  InvoiceDetailModel? _selectedInvoice;

  InvoiceState get state => _state;
  String? get errorMessage => _errorMessage;
  InvoiceFilter get filter => _filter;
  List<InvoiceModel> get invoices => List.unmodifiable(_invoices);
  bool get hasInvoices => _invoices.isNotEmpty;
  bool get isLoading => _state == InvoiceState.loading;
  bool get isLoadingMore => _state == InvoiceState.loadingMore;
  bool get hasMore => _invoices.length < _totalCount;
  int get totalCount => _totalCount;
  InvoiceDetailModel? get selectedInvoice => _selectedInvoice;

  /// Convenience: sum of unpaid balances in the *currently loaded* page set.
  /// For dashboard use, prefer a dedicated `/outstanding-total` endpoint
  /// (not yet implemented) over derived totals.
  double get outstandingBalance => _invoices
      .where((i) => !i.isPaid)
      .fold<double>(0.0, (sum, i) => sum + i.balance);

  /// Drop all tenant data (logout). See `resetSessionState`.
  void reset() {
    _state = InvoiceState.initial;
    _errorMessage = null;
    _filter = InvoiceFilter.all;
    _invoices.clear();
    _pageNumber = 1;
    _totalCount = 0;
    _selectedInvoice = null;
    notifyListeners();
  }

  /// Switch filter. Resets pagination and reloads page 1.
  Future<void> setFilter(int tenantId, InvoiceFilter newFilter) async {
    if (newFilter == _filter && _invoices.isNotEmpty) return;
    _filter = newFilter;
    await refresh(tenantId);
  }

  /// Reload page 1 for the current filter.
  Future<void> refresh(int tenantId) async {
    _state = InvoiceState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      final result = await _apiService.getInvoices(
        tenantId,
        status: _filter.apiValue,
      );
      _invoices
        ..clear()
        ..addAll(result.invoices);
      _pageNumber = 1;
      _totalCount = result.totalCount;
      _state = InvoiceState.loaded;
    } catch (e, stack) {
      _errorMessage = e.toString();
      _state = InvoiceState.error;
      if (kDebugMode) {
        // ignore: avoid_print
        print('InvoiceViewModel.refresh failed: $e\n$stack');
      }
    }
    notifyListeners();
  }

  /// Append the next page. No-op if a load is already in flight or there are
  /// no more pages.
  Future<void> loadMore(int tenantId) async {
    if (_state == InvoiceState.loading || _state == InvoiceState.loadingMore) return;
    if (!hasMore) return;

    _state = InvoiceState.loadingMore;
    notifyListeners();

    try {
      final next = _pageNumber + 1;
      final result = await _apiService.getInvoices(
        tenantId,
        status: _filter.apiValue,
        pageNumber: next,
      );
      _invoices.addAll(result.invoices);
      _pageNumber = next;
      _totalCount = result.totalCount;
      _state = InvoiceState.loaded;
    } catch (e, stack) {
      _errorMessage = e.toString();
      _state = InvoiceState.error;
      if (kDebugMode) {
        // ignore: avoid_print
        print('InvoiceViewModel.loadMore failed: $e\n$stack');
      }
    }
    notifyListeners();
  }

  /// Fetch a single invoice's detail (with applied payments).
  Future<void> loadInvoiceDetails(int tenantId, int invoiceId) async {
    _state = InvoiceState.loading;
    _errorMessage = null;
    _selectedInvoice = null;
    notifyListeners();

    try {
      _selectedInvoice = await _apiService.getInvoiceDetails(tenantId, invoiceId);
      _state = InvoiceState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = InvoiceState.error;
    }
    notifyListeners();
  }

  void clearSelectedInvoice() {
    _selectedInvoice = null;
    notifyListeners();
  }
}
