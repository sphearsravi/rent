import 'package:flutter/foundation.dart';
import '../../data/models/auth_model.dart';
import '../../data/services/tenant_api_service.dart';
import '../../data/services/storage_service.dart';

/// Authentication state enum
enum AuthState {
  initial,
  loading,
  authenticated,
  unauthenticated,
  error,
}

/// ViewModel for authentication operations
class AuthViewModel extends ChangeNotifier {
  final TenantApiService _apiService;
  final StorageService _storageService;

  AuthState _state = AuthState.initial;
  String? _errorMessage;
  int? _tenantId;
  String? _tenantName;
  String? _tenantEmail;

  AuthViewModel(this._apiService, this._storageService);

  // Getters
  AuthState get state => _state;
  String? get errorMessage => _errorMessage;
  int? get tenantId => _tenantId;
  String? get tenantName => _tenantName;
  String? get tenantEmail => _tenantEmail;
  bool get isAuthenticated => _state == AuthState.authenticated;
  bool get isLoading => _state == AuthState.loading;

  /// Check if user is already logged in. On every cold start, if a token is
  /// stored, silently renew it via /tenant/refresh — this keeps the session
  /// alive across the access-token TTL without forcing a re-login each time.
  /// If renewal fails (token fully expired or server rejected) we drop back
  /// to the unauthenticated state and the user lands on the login screen.
  Future<void> checkAuthStatus() async {
    _state = AuthState.loading;
    notifyListeners();

    try {
      final token = await _storageService.getToken();
      final tenantId = _storageService.getTenantId();
      if (token == null || token.isEmpty || tenantId == null) {
        _state = AuthState.unauthenticated;
        notifyListeners();
        return;
      }

      // Attempt to refresh — Dio's interceptor attaches the existing token,
      // server validates + reissues. 401 here means token is fully expired.
      final ok = await _apiService.refreshToken();
      if (!ok) {
        await _storageService.deleteToken();
        _state = AuthState.unauthenticated;
        notifyListeners();
        return;
      }

      _tenantId = tenantId;
      _tenantName = _storageService.getTenantName();
      _tenantEmail = _storageService.getTenantEmail();
      _state = AuthState.authenticated;
    } catch (e) {
      _state = AuthState.unauthenticated;
    }
    notifyListeners();
  }

  /// Login with Civil ID and password
  Future<bool> login(String civilId, String password, {bool rememberMe = false}) async {
    _state = AuthState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      final request = LoginRequest(
        civilId: civilId,
        password: password,
        rememberMe: rememberMe,
      );

      final response = await _apiService.login(request);

      if (response.success && response.tenantId != null) {
        // Save tenant data
        await _storageService.saveTenantId(response.tenantId!);
        await _storageService.saveTenantName(response.fullName);
        if (response.email != null) {
          await _storageService.saveTenantEmail(response.email!);
        }
        // Persist the JWT so every subsequent Dio call carries the Authorization header.
        if (response.token != null && response.token!.isNotEmpty) {
          await _storageService.saveToken(response.token!);
        }
        await _storageService.saveRememberMe(rememberMe);

        _tenantId = response.tenantId;
        _tenantName = response.fullName;
        _tenantEmail = response.email;
        _state = AuthState.authenticated;
        notifyListeners();
        return true;
      } else {
        _errorMessage = response.message;
        _state = AuthState.error;
        notifyListeners();
        return false;
      }
    } catch (e) {
      _errorMessage = e.toString();
      _state = AuthState.error;
      notifyListeners();
      return false;
    }
  }

  /// Change password
  Future<bool> changePassword(String oldPassword, String newPassword) async {
    if (_tenantId == null) {
      _errorMessage = 'Not logged in';
      return false;
    }

    _state = AuthState.loading;
    notifyListeners();

    try {
      final request = ChangePasswordRequest(
        tenantId: _tenantId!,
        oldPassword: oldPassword,
        newPassword: newPassword,
      );

      final success = await _apiService.changePassword(request);

      if (success) {
        _state = AuthState.authenticated;
        notifyListeners();
        return true;
      } else {
        _errorMessage = 'Failed to change password';
        _state = AuthState.error;
        notifyListeners();
        return false;
      }
    } catch (e) {
      _errorMessage = e.toString();
      _state = AuthState.error;
      notifyListeners();
      return false;
    }
  }

  /// Logout
  Future<void> logout() async {
    _state = AuthState.loading;
    notifyListeners();

    await _storageService.clearSession();
    _tenantId = null;
    _tenantName = null;
    _tenantEmail = null;
    _state = AuthState.unauthenticated;
    notifyListeners();
  }

  /// Clear error message
  void clearError() {
    _errorMessage = null;
    if (_state == AuthState.error) {
      _state = AuthState.unauthenticated;
    }
    notifyListeners();
  }
}
