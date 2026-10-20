import 'package:flutter/foundation.dart' show defaultTargetPlatform, TargetPlatform;
import 'package:flutter/material.dart';
import '../../l10n/app_localizations.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/platform_url_launcher.dart';

/// Non-dismissible "you must update" wall. Shown by [SplashScreen] when the
/// server's `minSupportedVersion` is newer than the running build. There is
/// deliberately no way past this screen except updating — `PopScope` blocks
/// the back button too.
class ForceUpdateScreen extends StatelessWidget {
  const ForceUpdateScreen({super.key, required this.storeUrl});

  final String storeUrl;

  /// Server URL when provided, otherwise the platform's store listing so the
  /// tenant can always update.
  String get _effectiveStoreUrl {
    if (storeUrl.trim().isNotEmpty) return storeUrl.trim();
    return defaultTargetPlatform == TargetPlatform.iOS
        ? 'https://testflight.apple.com/join/kSdrzSEX'
        : 'https://play.google.com/store/apps/details?id=com.alrtam';
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: false,
      child: Scaffold(
        body: Container(
          decoration: const BoxDecoration(gradient: AppTheme.primaryGradient),
          child: SafeArea(
            child: Center(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(32),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    // Brand logo (same asset as the app icon) with an update badge.
                    Stack(
                      alignment: Alignment.center,
                      children: [
                        ClipRRect(
                          borderRadius: BorderRadius.circular(24),
                          child: Image.asset('assets/icons/app_icon.png',
                              width: 96, height: 96, fit: BoxFit.cover,),
                        ),
                        PositionedDirectional(
                          end: 0,
                          bottom: 0,
                          child: Container(
                            padding: const EdgeInsets.all(6),
                            decoration: const BoxDecoration(
                                color: Colors.white, shape: BoxShape.circle,),
                            child: const Icon(Icons.system_update_rounded,
                                size: 20, color: AppTheme.primaryIndigo,),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 24),
                    Text(
                      AppLocalizations.of(context)!.forceUpdateTitle,
                      textAlign: TextAlign.center,
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 26,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      AppLocalizations.of(context)!.forceUpdateBody,
                      textAlign: TextAlign.center,
                      style: const TextStyle(color: Colors.white70, fontSize: 15),
                    ),
                    const SizedBox(height: 32),
                    // Always offer a way out. If the server sent no store URL
                    // the wall used to show NO button and trap the user.
                    SizedBox(
                        width: double.infinity,
                        child: ElevatedButton.icon(
                          onPressed: () => launchExternalUrl(_effectiveStoreUrl),
                          icon: const Icon(Icons.download_rounded),
                          label: Text(AppLocalizations.of(context)!.updateNow),
                          style: ElevatedButton.styleFrom(
                            backgroundColor: Colors.white,
                            foregroundColor: AppTheme.primaryIndigo,
                            padding: const EdgeInsets.symmetric(vertical: 16),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
