/// Stub implementation for non-web platforms
/// Uses url_launcher package to open URLs
library;
import 'package:flutter/foundation.dart';
import 'package:url_launcher/url_launcher.dart';

Future<void> launchExternalUrl(String url) async {
  final uri = Uri.parse(url);
  try {
    // Skip canLaunchUrl on Android: it requires <queries> entries in the
    // manifest for each scheme. We already declared http/https/tel/mailto,
    // but launchUrl can launch even if canLaunchUrl reports false in some
    // edge cases. Better to attempt and surface the failure.
    final launched = await launchUrl(uri, mode: LaunchMode.externalApplication);
    if (!launched && kDebugMode) {
      // ignore: avoid_print
      print('launchExternalUrl: launchUrl returned false for $url');
    }
  } catch (e, stack) {
    if (kDebugMode) {
      // ignore: avoid_print
      print('launchExternalUrl failed for $url: $e\n$stack');
    }
    rethrow;
  }
}

void redirectToUrl(String url) {
  launchExternalUrl(url);
}
