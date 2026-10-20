import 'package:flutter/foundation.dart';
import '../../data/models/tenant_model.dart';
import '../../data/services/tenant_api_service.dart';

/// Profile state enum
enum ProfileState {
  initial,
  loading,
  loaded,
  updating,
  updated,
  error,
}

/// ViewModel for profile operations
class ProfileViewModel extends ChangeNotifier {
  final TenantApiService _apiService;

  ProfileState _state = ProfileState.initial;
  String? _errorMessage;
  TenantModel? _profile;

  ProfileViewModel(this._apiService);

  // Getters
  ProfileState get state => _state;
  String? get errorMessage => _errorMessage;
  TenantModel? get profile => _profile;
  bool get isLoading => _state == ProfileState.loading;
  bool get isUpdating => _state == ProfileState.updating;
  bool get hasProfile => _profile != null;

  /// Drop all tenant data (logout). See `resetSessionState`.
  void reset() {
    _state = ProfileState.initial;
    _errorMessage = null;
    _profile = null;
    notifyListeners();
  }

  /// Load profile
  Future<void> loadProfile(int tenantId) async {
    _state = ProfileState.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      _profile = await _apiService.getProfile(tenantId);
      _state = ProfileState.loaded;
    } catch (e) {
      _errorMessage = e.toString();
      _state = ProfileState.error;
    }
    notifyListeners();
  }

  /// Update profile
  Future<bool> updateProfile(
    int tenantId, {
    String? email,
    String? phone,
    String? emergencyContact,
    String? emergencyPhone,
  }) async {
    _state = ProfileState.updating;
    _errorMessage = null;
    notifyListeners();

    try {
      final data = <String, dynamic>{};
      if (email != null) data['email'] = email;
      if (phone != null) data['phone'] = phone;
      if (emergencyContact != null) data['emergencyContactName'] = emergencyContact;
      if (emergencyPhone != null) data['emergencyContactPhone'] = emergencyPhone;

      final success = await _apiService.updateProfile(tenantId, data);

      if (success) {
        // Update local profile
        _profile = _profile?.copyWith(
          email: email ?? _profile?.email,
          phone: phone ?? _profile?.phone,
          emergencyContact: emergencyContact ?? _profile?.emergencyContact,
          emergencyPhone: emergencyPhone ?? _profile?.emergencyPhone,
        );
        _state = ProfileState.updated;
        notifyListeners();
        return true;
      } else {
        _errorMessage = 'Failed to update profile';
        _state = ProfileState.error;
        notifyListeners();
        return false;
      }
    } catch (e) {
      _errorMessage = e.toString();
      _state = ProfileState.error;
      notifyListeners();
      return false;
    }
  }

  /// Clear error
  void clearError() {
    _errorMessage = null;
    notifyListeners();
  }

  /// Refresh profile
  Future<void> refresh(int tenantId) async {
    await loadProfile(tenantId);
  }

  /// Reset state
  void resetState() {
    _state = ProfileState.loaded;
    notifyListeners();
  }
}
