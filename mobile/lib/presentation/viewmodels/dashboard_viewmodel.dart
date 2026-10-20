import 'package:flutter/foundation.dart';
import '../../data/models/dashboard_model.dart';
import '../../data/models/payment_model.dart'; // InvoiceModel
import '../../data/models/lease_model.dart';
import '../../data/services/tenant_api_service.dart';

/// Dashboard state enum
enum DashboardState {
  initial,
  loading,
  loaded,
  error,
}

/// ViewModel for dashboard operations
class DashboardViewModel extends ChangeNotifier {
  final TenantApiService _apiService;

  DashboardState _state = DashboardState.initial;
  String? _errorMessage;
  DashboardModel? _dashboardData;
  List<NotificationModel> _notifications = [];
  // Outstanding (Pending + Overdue) invoices — the single most useful thing a
  // tenant wants on the dashboard: what they owe and when it's due.
  List<InvoiceModel> _outstandingInvoices = [];
  // Whether the outstanding-invoice fetch has completed / failed. An empty
  // list only means "all paid up" once it has actually loaded successfully.
  bool _outstandingLoaded = false;
  bool _outstandingFailed = false;

  DashboardViewModel(this._apiService);

  // Getters
  DashboardState get state => _state;
  String? get errorMessage => _errorMessage;
  DashboardModel? get dashboardData => _dashboardData;
  List<NotificationModel> get notifications => _notifications;
  bool get isLoading => _state == DashboardState.loading;
  bool get hasData => _dashboardData != null;

  // Statistics shortcuts
  int get activeLeasesCount => _dashboardData?.statistics.activeLeasesCount ?? 0;
  int get totalPaymentsCount => _dashboardData?.statistics.totalPaymentsCount ?? 0;
  int get pendingMaintenanceCount => _dashboardData?.statistics.pendingMaintenanceRequestsCount ?? 0;
  double get totalMonthlyRent => _dashboardData?.totalMonthlyRent ?? 0;

  // ===== Account summary (the "what do I owe / when" block) =====

  List<InvoiceModel> get outstandingInvoices => _outstandingInvoices;

  /// Total amount the tenant currently owes across all unpaid invoices.
  double get totalOutstanding =>
      _outstandingInvoices.fold(0.0, (sum, i) => sum + i.balance);

  List<InvoiceModel> get overdueInvoices =>
      _outstandingInvoices.where((i) => i.isOverdue).toList();

  int get overdueCount => overdueInvoices.length;

  double get overdueAmount =>
      overdueInvoices.fold(0.0, (sum, i) => sum + i.balance);

  bool get hasOutstanding => _outstandingInvoices.isNotEmpty;

  /// True only when the outstanding fetch SUCCEEDED and came back empty. A
  /// failed or not-yet-finished fetch is "unknown" — never shown as paid up.
  bool get isAllPaidUp =>
      _outstandingLoaded && !_outstandingFailed && _outstandingInvoices.isEmpty;

  /// The balance block couldn't be loaded (network/server). UI shows a neutral
  /// "couldn't load balance" state instead of a figure.
  bool get outstandingUnknown => _outstandingFailed && _outstandingInvoices.isEmpty;

  /// The next invoice the tenant should pay — the one with the earliest due
  /// date (overdue ones naturally sort first). Null when all paid.
  InvoiceModel? get nextDueInvoice {
    if (_outstandingInvoices.isEmpty) return null;
    final sorted = [..._outstandingInvoices]
      ..sort((a, b) => a.dueDate.compareTo(b.dueDate));
    return sorted.first;
  }

  /// Whole days until the next invoice is due (negative = overdue by N days).
  int? get daysUntilNextDue {
    final inv = nextDueInvoice;
    if (inv == null) return null;
    final today = DateTime.now();
    final due = inv.dueDate;
    return DateTime(due.year, due.month, due.day)
        .difference(DateTime(today.year, today.month, today.day))
        .inDays;
  }

  /// Soonest-expiring active lease within 30 days (for a renewal nudge).
  LeaseModel? get expiringLease {
    final expiring = (_dashboardData?.activeLeases ?? [])
        .where((l) => l.isExpiring)
        .toList()
      ..sort((a, b) => a.daysRemaining.compareTo(b.daysRemaining));
    return expiring.isEmpty ? null : expiring.first;
  }

  /// Load dashboard data
  Future<void> loadDashboard(int tenantId) async {
    _state = DashboardState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      _dashboardData = await _apiService.getDashboard(tenantId);
      _state = DashboardState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = DashboardState.error;
    }
    notifyListeners();
  }

  /// Load the tenant's outstanding (Pending + Overdue) invoices so the
  /// dashboard can show the balance, overdue total and next due date.
  /// Silent-fails to an empty list — a dashboard without this block is still
  /// usable and must never error out the whole screen.
  Future<void> loadOutstandingInvoices(int tenantId) async {
    try {
      final result = await _apiService.getInvoices(
        tenantId,
        status: 'Outstanding',
        pageSize: 100,
      );
      _outstandingInvoices = result.invoices;
      _outstandingLoaded = true;
      _outstandingFailed = false;
    } catch (_) {
      // Unknown, not "nothing owed": keep whatever we had and flag the failure
      // so the UI never claims "all paid up" on a failed fetch.
      _outstandingFailed = true;
    }
    // Must notify — inside Future.wait this may finish AFTER loadDashboard,
    // and without it the UI kept the empty list and showed "all paid up".
    notifyListeners();
  }

  /// Load notifications
  Future<void> loadNotifications(int tenantId) async {
    try {
      _notifications = await _apiService.getNotifications(tenantId);
      notifyListeners();
    } catch (e) {
      // Silent fail for notifications
      _notifications = [];
    }
  }

  /// Refresh dashboard data
  Future<void> refresh(int tenantId) async {
    await Future.wait([
      loadDashboard(tenantId),
      loadOutstandingInvoices(tenantId),
      loadNotifications(tenantId),
    ]);
  }

  /// Clear error
  void clearError() {
    _errorMessage = null;
    notifyListeners();
  }

  /// Drop all tenant data (logout). See `resetSessionState`.
  void reset() {
    _state = DashboardState.initial;
    _errorMessage = null;
    _dashboardData = null;
    _notifications = [];
    _outstandingInvoices = [];
    _outstandingLoaded = false;
    _outstandingFailed = false;
    notifyListeners();
  }
}
