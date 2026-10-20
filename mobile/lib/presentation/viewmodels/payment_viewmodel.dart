import 'package:flutter/foundation.dart';
import 'package:intl/intl.dart';
import '../../data/models/payment_model.dart';
import '../../data/services/api_service.dart';
import '../../data/services/tenant_api_service.dart';

/// Payment state enum
enum PaymentState {
  initial,
  loading,
  loaded,
  submitting,
  submitted,
  error,
}

/// ViewModel for payment operations
class PaymentViewModel extends ChangeNotifier {
  final TenantApiService _apiService;

  PaymentState _state = PaymentState.initial;
  String? _errorMessage;
  // Sprint 26 — set when the server returns 409 (pay-oldest-first). The
  // UI uses these to render a "Pay invoice #X first" call-to-action that
  // jumps the tenant to the right invoice.
  int? _blockedByInvoiceId;
  double? _blockedByBalance;
  List<PaymentModel> _payments = [];
  List<InvoiceModel> _pendingInvoices = [];
  PaymentModel? _selectedPayment;
  int _currentPage = 1;
  bool _hasMorePages = true;
  int? _tenantId;

  PaymentViewModel(this._apiService);

  // Getters
  PaymentState get state => _state;
  String? get error => _errorMessage;
  String? get errorMessage => _errorMessage;
  List<PaymentModel> get payments => _payments;
  List<InvoiceModel> get pendingInvoices => _pendingInvoices;
  PaymentModel? get selectedPayment => _selectedPayment;
  bool get isLoading => _state == PaymentState.loading;
  bool get isSubmitting => _state == PaymentState.submitting;
  bool get hasPayments => _payments.isNotEmpty;
  bool get hasMorePages => _hasMorePages;
  // Sprint 26 — 409 surface
  int? get blockedByInvoiceId => _blockedByInvoiceId;
  double? get blockedByBalance => _blockedByBalance;
  bool get isPaymentBlocked => _blockedByInvoiceId != null;

  /// Set tenant ID for subsequent API calls
  void setTenantId(int tenantId) {
    _tenantId = tenantId;
  }

  /// Drop all tenant data (logout). See `resetSessionState`.
  void reset() {
    _state = PaymentState.initial;
    _errorMessage = null;
    _blockedByInvoiceId = null;
    _blockedByBalance = null;
    _payments = [];
    _pendingInvoices = [];
    _selectedPayment = null;
    _currentPage = 1;
    _hasMorePages = true;
    _tenantId = null;
    notifyListeners();
  }

  /// Completed payments
  List<PaymentModel> get completedPayments =>
      _payments.where((p) => p.isCompleted).toList();

  /// Pending payments
  List<PaymentModel> get pendingPayments =>
      _payments.where((p) => p.isPending).toList();

  /// Total paid amount
  double get totalPaidAmount =>
      completedPayments.fold(0.0, (sum, p) => sum + p.amountPaid);

  /// Load payment history
  Future<void> loadPayments(int tenantId, {bool refresh = false}) async {
    if (refresh) {
      _currentPage = 1;
      _hasMorePages = true;
      _payments = [];
    }

    if (!_hasMorePages && !refresh) return;

    _state = PaymentState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      final newPayments = await _apiService.getPayments(
        tenantId,
        pageNumber: _currentPage,
      );

      if (newPayments.length < 20) {
        _hasMorePages = false;
      }

      if (refresh) {
        _payments = newPayments;
      } else {
        _payments.addAll(newPayments);
      }

      _currentPage++;
      _state = PaymentState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = PaymentState.error;
    }
    notifyListeners();
  }

  /// Load more payments (pagination)
  Future<void> loadMorePayments(int tenantId) async {
    if (_state == PaymentState.loading || !_hasMorePages) return;
    await loadPayments(tenantId);
  }

  /// Load payment details
  Future<void> loadPaymentDetails(int tenantId, int paymentId) async {
    _state = PaymentState.loading;
    _errorMessage = null;
    // Clear the previous payment FIRST. Otherwise a failed poll leaves an older
    // completed payment in place and the result screen reports "Payment
    // successful" for a payment that never settled.
    _selectedPayment = null;
    notifyListeners();

    try {
      _selectedPayment = await _apiService.getPaymentDetails(tenantId, paymentId);
      _state = PaymentState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = PaymentState.error;
      // Surface the failure to the caller so the result screen can retry
      // instead of silently reading a null/stale payment.
      rethrow;
    } finally {
      notifyListeners();
    }
  }

  /// Select a payment from the list
  void selectPayment(PaymentModel payment) {
    _selectedPayment = payment;
    notifyListeners();
  }

  /// Clear selected payment
  void clearSelectedPayment() {
    _selectedPayment = null;
    notifyListeners();
  }

  /// Clear error
  void clearError() {
    _errorMessage = null;
    notifyListeners();
  }

  /// Refresh payments
  Future<void> refresh(int tenantId) async {
    await loadPayments(tenantId, refresh: true);
  }

  /// Reset state
  void resetState() {
    _state = PaymentState.loaded;
    notifyListeners();
  }

  /// Load pending invoices for payment
  Future<void> loadPendingInvoices() async {
    if (_tenantId == null) {
      _errorMessage = 'Tenant ID not set';
      _state = PaymentState.error;
      notifyListeners();
      return;
    }

    _state = PaymentState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      _pendingInvoices = await _apiService.getPendingInvoices(_tenantId!);
      _state = PaymentState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = PaymentState.error;
    }
    notifyListeners();
  }

  /// Initiate KNET payment - returns response with redirect URL
  Future<KnetPaymentResponse?> initiateKNETPayment({
    required InvoiceModel invoice,
    required int tenantId,
  }) async {
    _state = PaymentState.submitting;
    _errorMessage = null;
    _blockedByInvoiceId = null;
    _blockedByBalance = null;
    notifyListeners();

    try {
      final request = KnetPaymentRequest(
        invoiceId: invoice.invoiceId,
        tenantId: tenantId,
        propertyId: invoice.propertyId ?? 0,
        leaseId: invoice.leaseId ?? 0,
        // Pay what is still OWED, not the invoice total. The server rejects any
        // amount above Total − Paid, so sending totalAmount made KNET fail for
        // every partially-paid invoice.
        amount: invoice.balance,
        // Send the app's current language so the KNET gateway + the
        // success/failure landing page render in Arabic when the tenant
        // is using the app in Arabic. Intl.defaultLocale is kept in sync
        // by LocaleProvider.
        language: (Intl.defaultLocale ?? 'en').startsWith('ar') ? 'ar' : 'en',
      );

      final response = await _apiService.initiateKNETPayment(request);

      if (response.success && response.redirectUrl != null && response.redirectUrl!.isNotEmpty) {
        _state = PaymentState.loaded;
        notifyListeners();
        return response;
      } else {
        _errorMessage = response.errorMessage ?? 'Failed to initiate KNET payment';
        _state = PaymentState.error;
        notifyListeners();
        return response;
      }
    } on ApiException catch (e) {
      // Sprint 26 — server's pay-oldest-first rule. Surface the blocking
      // invoice info so the UI can offer a "Pay invoice #N first" action.
      if (e.kind == ApiErrorKind.paymentBlocked) {
        _blockedByInvoiceId = e.oldestInvoiceId;
        _blockedByBalance = e.oldestBalance;
      }
      _errorMessage = e.message;
      _state = PaymentState.error;
      notifyListeners();
      return null;
    } catch (e) {
      _errorMessage = e.toString();
      _state = PaymentState.error;
      notifyListeners();
      return null;
    }
  }

  /// Submit bank transfer details.
  ///
  /// [tenantId] is optional: the server derives the tenant from the JWT and
  /// ignores the body value (it's only there for the DTO binder). The old code
  /// refused to call the API unless `setTenantId()` had been called — and that
  /// only happened on the legacy "pick an invoice" path, so the normal
  /// "Pay now from an invoice" flow always failed with "Tenant ID not set".
  Future<bool> submitBankTransfer({
    required int invoiceId,
    required double amount,
    required String bankName,
    required DateTime transferDate,
    required String proofFilePath,
    String? referenceNumber,
    String? notes,
    int? tenantId,
  }) async {
    final effectiveTenantId = tenantId ?? _tenantId ?? 0;
    if (tenantId != null) _tenantId = tenantId;

    _state = PaymentState.submitting;
    _errorMessage = null;
    _blockedByInvoiceId = null;
    _blockedByBalance = null;
    notifyListeners();

    try {
      final success = await _apiService.submitBankTransfer(
        tenantId: effectiveTenantId,
        invoiceId: invoiceId,
        amount: amount,
        bankName: bankName,
        transferDate: transferDate,
        proofFilePath: proofFilePath,
        referenceNumber: referenceNumber,
        notes: notes,
      );

      if (success) {
        _state = PaymentState.submitted;
        notifyListeners();
        return true;
      } else {
        _errorMessage = 'Failed to submit bank transfer';
        _state = PaymentState.error;
        notifyListeners();
        return false;
      }
    } on ApiException catch (e) {
      // Sprint 26 — pay-oldest-first rule on the bank-transfer path too.
      if (e.kind == ApiErrorKind.paymentBlocked) {
        _blockedByInvoiceId = e.oldestInvoiceId;
        _blockedByBalance = e.oldestBalance;
      }
      _errorMessage = e.message;
      _state = PaymentState.error;
      notifyListeners();
      return false;
    } catch (e) {
      _errorMessage = e.toString();
      _state = PaymentState.error;
      notifyListeners();
      return false;
    }
  }

  /// Tenant requests to pay an invoice in cash at the office. Creates a
  /// pending cash record the office then confirms. Surfaces the same
  /// pay-oldest-first (409) block as the other methods.
  ///
  /// [amount] null = full balance; a smaller value declares a partial payment.
  Future<bool> requestCashPayment({
    required int invoiceId,
    double? amount,
    String? notes,
  }) async {
    _state = PaymentState.submitting;
    _errorMessage = null;
    _blockedByInvoiceId = null;
    _blockedByBalance = null;
    notifyListeners();

    try {
      final ok = await _apiService.requestCashPayment(
        invoiceId: invoiceId,
        amount: amount,
        notes: notes,
      );
      if (ok) {
        _state = PaymentState.submitted;
      } else {
        _errorMessage = 'Failed to request cash payment';
        _state = PaymentState.error;
      }
      notifyListeners();
      return ok;
    } on ApiException catch (e) {
      if (e.kind == ApiErrorKind.paymentBlocked) {
        _blockedByInvoiceId = e.oldestInvoiceId;
        _blockedByBalance = e.oldestBalance;
      }
      _errorMessage = e.message;
      _state = PaymentState.error;
      notifyListeners();
      return false;
    } catch (e) {
      _errorMessage = e.toString();
      _state = PaymentState.error;
      notifyListeners();
      return false;
    }
  }

  /// Sprint 26 — clear the blocked-payment state, e.g. after the user has
  /// navigated to the older invoice and we want a fresh attempt.
  void clearBlockedPayment() {
    _blockedByInvoiceId = null;
    _blockedByBalance = null;
    notifyListeners();
  }
}
