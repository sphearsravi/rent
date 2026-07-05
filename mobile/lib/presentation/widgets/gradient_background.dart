import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';

/// A widget that provides the indigo/purple gradient background
class GradientBackground extends StatelessWidget {
  final Widget child;
  final bool useGradient;

  const GradientBackground({
    super.key,
    required this.child,
    this.useGradient = true,
  });

  @override
  Widget build(BuildContext context) {
    if (!useGradient) {
      return Container(
        color: AppTheme.backgroundLight,
        child: child,
      );
    }

    return Container(
      decoration: const BoxDecoration(
        gradient: AppTheme.backgroundGradient,
      ),
      child: child,
    );
  }
}
