import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../data/services/storage_service.dart';

/// Holds the user-selected app language and persists it. When unset, the
/// app falls back to the device locale (handled by passing `null` to
/// MaterialApp.locale). Arabic automatically renders RTL because the
/// localization delegates are wired and `ar` is a supported locale.
class LocaleProvider extends ChangeNotifier {
  static const _storageKey = 'app_locale';

  final StorageService _storage;
  Locale? _locale;

  LocaleProvider(this._storage) {
    final saved = _storage.getString(_storageKey);
    if (saved == 'en' || saved == 'ar') {
      _locale = Locale(saved!);
    }
    // Keep intl's default locale in sync so AppDateUtils formats month/day
    // names in the active language (with Western digits) from first frame.
    Intl.defaultLocale = languageCode;
  }

  /// Null = follow the device locale.
  Locale? get locale => _locale;

  /// Language code currently in effect for the UI. Used by the toggle to
  /// decide which label to show. Defaults to 'en' when following the device.
  String get languageCode => _locale?.languageCode ?? 'en';

  bool get isArabic => languageCode == 'ar';

  Future<void> setLanguage(String code) async {
    if (code != 'en' && code != 'ar') return;
    _locale = Locale(code);
    Intl.defaultLocale = code;
    await _storage.setString(_storageKey, code);
    notifyListeners();
  }

  Future<void> toggle() => setLanguage(isArabic ? 'en' : 'ar');
}
