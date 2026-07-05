import 'dart:async';
import 'dart:io' show Platform;

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';

import 'api_service.dart';

/// Sprint 26 — Firebase Cloud Messaging wrapper. Owns one job:
/// 1. Initialize Firebase on app start
/// 2. Ask the OS for notification permission (Android 13+, iOS)
/// 3. Fetch the FCM device token
/// 4. POST it to `/api/tenant/{tenantId}/devices` so the server can
///    target the device when a notification gets enqueued
/// 5. Listen for token rotations + re-register
/// 6. Surface incoming foreground messages via [onMessage]
///
/// The implementation is **defensive** — every Firebase call is wrapped
/// in a try/catch because the project may have a placeholder
/// google-services.json (no real Firebase project linked yet, see
/// docs/PUSH_NOTIFICATIONS_SETUP.md). In that mode all calls fail silently;
/// the app keeps working, just without pushes.
class FcmService {
  FcmService(this._api);

  final ApiService _api;
  FirebaseMessaging? _messaging;
  bool _initialized = false;

  /// Fires when a push arrives while the app is in foreground. Wire to a
  /// banner / in-app toast.
  final StreamController<RemoteMessage> _foregroundController =
      StreamController<RemoteMessage>.broadcast();
  Stream<RemoteMessage> get onForegroundMessage => _foregroundController.stream;

  /// Call once at app startup BEFORE login. Idempotent — safe to call again
  /// after login when [registerWithServer] kicks in.
  Future<void> initialize() async {
    if (_initialized) return;
    try {
      await Firebase.initializeApp();
      _messaging = FirebaseMessaging.instance;

      // Ask the user for notification permission. On Android < 13 this is
      // a no-op; on Android 13+ it surfaces the OS prompt. On iOS it asks
      // for alert+badge+sound. Result is logged but not enforced — even
      // denied permission still lets us collect a token (just no UI shown).
      final settings = await _messaging!.requestPermission(
        alert: true,
        badge: true,
        sound: true,
        provisional: false,
      );
      debugPrint('[FCM] Permission: ${settings.authorizationStatus}');

      // Foreground message handler. Background is handled by the top-level
      // [firebaseMessagingBackgroundHandler] registered in main.dart.
      FirebaseMessaging.onMessage.listen((message) {
        debugPrint('[FCM] Foreground message: ${message.notification?.title}');
        _foregroundController.add(message);
      });

      _initialized = true;
    } catch (e) {
      // Placeholder google-services.json or no network on first launch.
      // Don't crash the app — just log. The user can still use everything
      // else; pushes simply won't arrive.
      debugPrint('[FCM] init failed (likely placeholder Firebase config): $e');
    }
  }

  /// Call AFTER successful login. Grabs the FCM token (one network round
  /// trip with Google) and POSTs it to the server so the server can target
  /// this device for push notifications.
  Future<void> registerWithServer(int tenantId) async {
    if (!_initialized) {
      await initialize();
    }
    if (_messaging == null) {
      debugPrint('[FCM] Skipping register — messaging not initialized');
      return;
    }

    try {
      final token = await _messaging!.getToken();
      if (token == null || token.isEmpty) {
        debugPrint('[FCM] No token returned (Firebase not configured?)');
        return;
      }
      debugPrint('[FCM] Token: ${token.substring(0, 16)}…');

      await _postToken(tenantId, token);

      // Re-register if the token rotates (FCM rotates rarely but can —
      // e.g. data wipe, app restore from backup).
      _messaging!.onTokenRefresh.listen((newToken) async {
        debugPrint('[FCM] Token refreshed: ${newToken.substring(0, 16)}…');
        await _postToken(tenantId, newToken);
      });
    } catch (e) {
      debugPrint('[FCM] register failed: $e');
    }
  }

  Future<void> _postToken(int tenantId, String token) async {
    try {
      await _api.post(
        '/tenant/$tenantId/devices',
        data: {
          'token': token,
          'platform': _platformString(),
          'deviceId': null, // TODO: device-info-plus to get a stable per-install ID
          'locale': PlatformDispatcher.instance.locale.toLanguageTag(),
        },
      );
      debugPrint('[FCM] Server registered token for tenant $tenantId');
    } catch (e) {
      // Server unreachable or 401 — re-tries on next app launch.
      debugPrint('[FCM] register call failed: $e');
    }
  }

  String _platformString() {
    if (kIsWeb) return 'web';
    try {
      if (Platform.isAndroid) return 'android';
      if (Platform.isIOS) return 'ios';
    } catch (_) {}
    return 'unknown';
  }

  void dispose() {
    _foregroundController.close();
  }
}

/// Top-level entry for background-isolate message handling. MUST be a
/// top-level function (not a method) — Flutter spawns a fresh isolate.
/// Called from main.dart via [FirebaseMessaging.onBackgroundMessage].
@pragma('vm:entry-point')
Future<void> firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  try {
    await Firebase.initializeApp();
    debugPrint('[FCM bg] ${message.notification?.title}');
  } catch (_) {
    // Background isolate — if Firebase isn't configured we just bail.
  }
}
