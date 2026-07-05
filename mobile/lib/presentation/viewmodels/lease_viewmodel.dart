import 'package:flutter/foundation.dart';
import '../../data/models/lease_model.dart';
import '../../data/services/tenant_api_service.dart';

/// Lease state enum
enum LeaseState {
  initial,
  loading,
  loaded,
  error,
}

/// ViewModel for lease operations
class LeaseViewModel extends ChangeNotifier {
  final TenantApiService _apiService;

  LeaseState _state = LeaseState.initial;
  String? _errorMessage;
  List<LeaseModel> _leases = [];
  LeaseModel? _selectedLease;

  LeaseViewModel(this._apiService);

  // Getters
  LeaseState get state => _state;
  String? get errorMessage => _errorMessage;
  List<LeaseModel> get leases => _leases;
  LeaseModel? get selectedLease => _selectedLease;
  bool get isLoading => _state == LeaseState.loading;
  bool get hasLeases => _leases.isNotEmpty;

  /// Active leases only
  List<LeaseModel> get activeLeases =>
      _leases.where((l) => l.isActive).toList();

  /// Expiring leases (within 30 days)
  List<LeaseModel> get expiringLeases =>
      _leases.where((l) => l.isExpiring).toList();

  /// Total monthly rent
  double get totalMonthlyRent =>
      _leases.fold(0.0, (sum, lease) => sum + lease.monthlyRent);

  /// Load all leases
  Future<void> loadLeases(int tenantId) async {
    _state = LeaseState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      _leases = await _apiService.getLeases(tenantId);
      _state = LeaseState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = LeaseState.error;
    }
    notifyListeners();
  }

  /// Load lease details
  Future<void> loadLeaseDetails(int tenantId, int leaseId) async {
    _state = LeaseState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      _selectedLease = await _apiService.getLeaseDetails(tenantId, leaseId);
      _state = LeaseState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = LeaseState.error;
    }
    notifyListeners();
  }

  /// Select a lease from the list
  void selectLease(LeaseModel lease) {
    _selectedLease = lease;
    notifyListeners();
  }

  /// Clear selected lease
  void clearSelectedLease() {
    _selectedLease = null;
    notifyListeners();
  }

  /// Clear error
  void clearError() {
    _errorMessage = null;
    notifyListeners();
  }

  /// Refresh leases
  Future<void> refresh(int tenantId) async {
    await loadLeases(tenantId);
  }
}
