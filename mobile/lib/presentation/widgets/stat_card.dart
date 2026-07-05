import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';

/// Statistics card widget for dashboard.
///
/// Vertical layout: rounded icon at the top, big number below, full label
/// at the bottom. Tested across 320–414px phone widths; nothing gets
/// ellipsized at 3-up.
class StatCard extends StatelessWidget {
  final IconData icon;
  final Color iconColor;
  final String title;
  final String value;
  final VoidCallback? onTap;

  const StatCard({
    super.key,
    required this.icon,
    required this.iconColor,
    required this.title,
    required this.value,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final screenWidth = MediaQuery.of(context).size.width;
    // 20px padding on each side of the row + 12px between 3 cards = 64px.
    final cardWidth = (screenWidth - 64) / 3;

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(16),
      child: Container(
        width: cardWidth,
        padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 12),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: iconColor.withOpacity(0.12)),
          boxShadow: [
            BoxShadow(
              color: iconColor.withOpacity(0.08),
              blurRadius: 16,
              offset: const Offset(0, 6),
            ),
          ],
        ),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            // Rounded soft-tinted icon
            Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(
                color: iconColor.withOpacity(0.14),
                borderRadius: BorderRadius.circular(14),
              ),
              child: Icon(icon, color: iconColor, size: 22),
            ),
            const SizedBox(height: 12),
            // Big number — owns the visual weight of the card
            Text(
              value,
              style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.w800,
                    color: AppTheme.textPrimary,
                    height: 1.1,
                  ),
            ),
            const SizedBox(height: 4),
            // Label — small but full text, wraps to two lines if needed
            Text(
              title,
              textAlign: TextAlign.center,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    color: AppTheme.textMuted,
                    height: 1.3,
                  ),
            ),
          ],
        ),
      ),
    );
  }
}
