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

    // Read the stored tenant id OUTSIDE the try so the offline `catch` below can
    // use it. (Previously the catch read the `tenantId` *getter*, which was
    // still null, so an offline launch "authenticated" with no tenant and every
    // tab loaded nothing.)
    final int? storedTenantId = _storageService.getTenantId();

    try {
      final token = await _storageService.getToken();
      final tenantId = storedTenantId;
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
      // The refresh threw a network/timeout error (offline launch). The stored
      // token is likely still valid, so keep the session rather than logging
      // out — the 401 interceptor will boot to login if it's genuinely expired
      // on the next real request.
      if (storedTenantId == null) {
        _state = AuthState.unauthenticated;
      } else {
        _tenantId = storedTenantId;
        _tenantName = _storageService.getTenantName();
        _tenantEmail = _storageService.getTenantEmail();
        _state = AuthState.authenticated;
      }
    }
    notifyListeners();
  }

  /// Load remembered login details for prefilling the login form. Returns the
  /// saved Civil ID + password only when "Remember me" was previously checked.
  Future<({bool rememberMe, String? civilId, String? password})>
      loadRememberedLogin() async {
    if (!_storageService.getRememberMe()) {
      return (rememberMe: false, civilId: null, password: null);
    }
    return (
      rememberMe: true,
      civilId: await _storageService.getSavedCivilId(),
      password: await _storageService.getSavedPassword(),
    );
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
        // "Remember me" → keep the credentials (encrypted) so the login form
        // can prefill them next time; otherwise forget any previously saved ones.
        if (rememberMe) {
          await _storageService.saveCredentials(civilId, password);
        } else {
          await _storageService.clearCredentials();
        }

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

  /// Separate from the session [state]: changing a password must never flip
  /// `isAuthenticated` to false while the user is still logged in (the old
  /// code reused AuthState.loading/error for this and briefly logged them out).
  bool _isChangingPassword = false;
  bool get isChangingPassword => _isChangingPassword;

  /// Change password. On success the service persists the server's new token;
  /// here we also refresh the remembered credentials so "Remember me" doesn't
  /// prefill the OLD password next launch.
  Future<bool> changePassword(String oldPassword, String newPassword) async {
    if (_tenantId == null) {
      _errorMessage = 'Not logged in';
      notifyListeners();
      return false;
    }

    _isChangingPassword = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final request = ChangePasswordRequest(
        tenantId: _tenantId!,
        oldPassword: oldPassword,
        newPassword: newPassword,
      );

      final success = await _apiService.changePassword(request);

      if (success) {
        if (_storageService.getRememberMe()) {
          final civilId = await _storageService.getSavedCivilId();
          if (civilId != null && civilId.isNotEmpty) {
            await _storageService.saveCredentials(civilId, newPassword);
          }
        }
        return true;
      }
      _errorMessage = 'Failed to change password';
      return false;
    } catch (e) {
      _errorMessage = e.toString();
      return false;
    } finally {
      _isChangingPassword = false;
      notifyListeners();
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
