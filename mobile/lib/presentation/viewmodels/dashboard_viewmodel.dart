import 'package:flutter/foundation.dart';
import '../../data/models/dashboard_model.dart';
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
      loadNotifications(tenantId),
    ]);
  }

  /// Clear error
  void clearError() {
    _errorMessage = null;
    notifyListeners();
  }
}
