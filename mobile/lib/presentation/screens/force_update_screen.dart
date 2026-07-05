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
                    const Icon(Icons.system_update_rounded,
                        size: 88, color: Colors.white),
                    const SizedBox(height: 24),
                    Text(
                      AppLocalizations.of(context)!.forceUpdateTitle,
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
                    if (storeUrl.isNotEmpty)
                      SizedBox(
                        width: double.infinity,
                        child: ElevatedButton.icon(
                          onPressed: () => launchExternalUrl(storeUrl),
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
