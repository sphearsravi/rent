import 'package:shared_preferences/shared_preferences.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import '../../core/constants/app_constants.dart';

/// Storage service for managing local data persistence
class StorageService {
  late SharedPreferences _prefs;
  final FlutterSecureStorage _secureStorage = const FlutterSecureStorage();

  /// Initialize SharedPreferences
  Future<void> init() async {
    _prefs = await SharedPreferences.getInstance();
  }

  // ============ Secure Storage (for sensitive data) ============

  /// Save authentication token
  Future<void> saveToken(String token) async {
    await _secureStorage.write(key: AppConstants.tokenKey, value: token);
  }

  /// Get authentication token
  Future<String?> getToken() async {
    return await _secureStorage.read(key: AppConstants.tokenKey);
  }

  /// Delete authentication token
  Future<void> deleteToken() async {
    await _secureStorage.delete(key: AppConstants.tokenKey);
  }

  // ============ SharedPreferences (for non-sensitive data) ============

  /// Save tenant ID
  Future<void> saveTenantId(int tenantId) async {
    await _prefs.setInt(AppConstants.tenantIdKey, tenantId);
  }

  /// Get tenant ID
  int? getTenantId() {
    return _prefs.getInt(AppConstants.tenantIdKey);
  }

  /// Save tenant name
  Future<void> saveTenantName(String name) async {
    await _prefs.setString(AppConstants.tenantNameKey, name);
  }

  /// Get tenant name
  String? getTenantName() {
    return _prefs.getString(AppConstants.tenantNameKey);
  }

  /// Save tenant email
  Future<void> saveTenantEmail(String email) async {
    await _prefs.setString(AppConstants.tenantEmailKey, email);
  }

  /// Get tenant email
  String? getTenantEmail() {
    return _prefs.getString(AppConstants.tenantEmailKey);
  }

  /// Save remember me preference
  Future<void> saveRememberMe(bool value) async {
    await _prefs.setBool(AppConstants.rememberMeKey, value);
  }

  /// Get remember me preference
  bool getRememberMe() {
    return _prefs.getBool(AppConstants.rememberMeKey) ?? false;
  }

  /// Check if user is logged in
  Future<bool> isLoggedIn() async {
    final tenantId = getTenantId();
    return tenantId != null && tenantId > 0;
  }

  /// Clear all stored data (logout)
  Future<void> clearAll() async {
    await _secureStorage.deleteAll();
    await _prefs.clear();
  }

  /// Clear session data but keep remember me preferences
  Future<void> clearSession() async {
    await _secureStorage.delete(key: AppConstants.tokenKey);
    await _prefs.remove(AppConstants.tenantIdKey);
    await _prefs.remove(AppConstants.tenantNameKey);
    await _prefs.remove(AppConstants.tenantEmailKey);
  }

  // ============ Generic methods ============

  /// Save string value
  Future<void> setString(String key, String value) async {
    await _prefs.setString(key, value);
  }

  /// Get string value
  String? getString(String key) {
    return _prefs.getString(key);
  }

  /// Save int value
  Future<void> setInt(String key, int value) async {
    await _prefs.setInt(key, value);
  }

  /// Get int value
  int? getInt(String key) {
    return _prefs.getInt(key);
  }

  /// Save bool value
  Future<void> setBool(String key, bool value) async {
    await _prefs.setBool(key, value);
  }

  /// Get bool value
  bool? getBool(String key) {
    return _prefs.getBool(key);
  }

  /// Remove a specific key
  Future<void> remove(String key) async {
    await _prefs.remove(key);
  }
}
