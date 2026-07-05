import 'package:flutter/foundation.dart';
import '../../data/models/maintenance_model.dart';
import '../../data/services/tenant_api_service.dart';

/// Maintenance state enum
enum MaintenanceState {
  initial,
  loading,
  loaded,
  submitting,
  submitted,
  error,
}

/// ViewModel for maintenance request operations
class MaintenanceViewModel extends ChangeNotifier {
  final TenantApiService _apiService;

  MaintenanceState _state = MaintenanceState.initial;
  String? _errorMessage;
  List<MaintenanceModel> _requests = [];
  MaintenanceModel? _selectedRequest;

  MaintenanceViewModel(this._apiService);

  // Getters
  MaintenanceState get state => _state;
  String? get errorMessage => _errorMessage;
  List<MaintenanceModel> get requests => _requests;
  MaintenanceModel? get selectedRequest => _selectedRequest;
  bool get isLoading => _state == MaintenanceState.loading;
  bool get isSubmitting => _state == MaintenanceState.submitting;
  bool get hasRequests => _requests.isNotEmpty;

  /// Open requests
  List<MaintenanceModel> get openRequests =>
      _requests.where((r) => r.isOpen).toList();

  /// In progress requests
  List<MaintenanceModel> get inProgressRequests =>
      _requests.where((r) => r.isInProgress).toList();

  /// Completed requests
  List<MaintenanceModel> get completedRequests =>
      _requests.where((r) => r.isCompleted).toList();

  /// Pending requests (open + in progress)
  List<MaintenanceModel> get pendingRequests =>
      _requests.where((r) => r.isOpen || r.isInProgress).toList();

  /// Load all maintenance requests
  Future<void> loadRequests(int tenantId, {String? status}) async {
    _state = MaintenanceState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      _requests = await _apiService.getMaintenanceRequests(tenantId, status: status);
      _state = MaintenanceState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = MaintenanceState.error;
    }
    notifyListeners();
  }

  /// Load request details
  Future<void> loadRequestDetails(int tenantId, int requestId) async {
    _state = MaintenanceState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      _selectedRequest = await _apiService.getMaintenanceRequestDetails(tenantId, requestId);
      _state = MaintenanceState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = MaintenanceState.error;
    }
    notifyListeners();
  }

  /// Create a new maintenance request. Optional `photoPaths` are uploaded
  /// after the request is created — failures there don't roll back the
  /// request itself (the tenant has already done the work of describing it).
  Future<bool> createRequest(
    int tenantId, {
    required int leaseId,
    required String category,
    required String priority,
    required String description,
    List<String> photoPaths = const [],
  }) async {
    _state = MaintenanceState.submitting;
    _errorMessage = null;
    notifyListeners();

    try {
      final request = CreateMaintenanceRequest(
        leaseId: leaseId,
        category: category,
        priority: priority,
        description: description,
      );

      final requestId = await _apiService.createMaintenanceRequest(tenantId, request);

      if (requestId > 0) {
        // Best-effort photo upload — request is already created; if photo
        // upload fails we still report success so the tenant doesn't see a
        // false "failed". Errors are recorded for inspection.
        if (photoPaths.isNotEmpty) {
          try {
            await _apiService.uploadMaintenancePhotos(
              tenantId: tenantId,
              requestId: requestId,
              filePaths: photoPaths,
            );
          } catch (e) {
            _errorMessage = 'Request created but photo upload failed: $e';
          }
        }
        _lastCreatedRequestId = requestId;
        _state = MaintenanceState.submitted;
        notifyListeners();
        return true;
      } else {
        _errorMessage = 'Failed to create request';
        _state = MaintenanceState.error;
        notifyListeners();
        return false;
      }
    } catch (e) {
      _errorMessage = e.toString();
      _state = MaintenanceState.error;
      notifyListeners();
      return false;
    }
  }

  int? _lastCreatedRequestId;
  int? get lastCreatedRequestId => _lastCreatedRequestId;

  /// Select a request from the list
  void selectRequest(MaintenanceModel request) {
    _selectedRequest = request;
    notifyListeners();
  }

  /// Clear selected request
  void clearSelectedRequest() {
    _selectedRequest = null;
    notifyListeners();
  }

  /// Clear error
  void clearError() {
    _errorMessage = null;
    notifyListeners();
  }

  /// Refresh requests
  Future<void> refresh(int tenantId) async {
    await loadRequests(tenantId);
  }

  /// Reset state
  void resetState() {
    _state = MaintenanceState.loaded;
    notifyListeners();
  }
}
