import 'package:flutter/material.dart';
import '../../core/utils/date_utils.dart';
import '../../core/utils/status_localizer.dart';
import '../../data/models/lease_model.dart';
import '../../l10n/app_localizations.dart';

/// Read-only notice shown to a tenant when a legal case is filed on their lease.
/// Renders nothing when there is no case. Amber while the case is open, grey
/// once it's closed/won/lost/settled/withdrawn. Never shows notes or amounts.
class LegalCaseBanner extends StatelessWidget {
  final LeaseModel lease;
  final bool compact;

  const LegalCaseBanner({super.key, required this.lease, this.compact = false});

  @override
  Widget build(BuildContext context) {
    if (!lease.hasLegalCase) return const SizedBox.shrink();

    final l = AppLocalizations.of(context)!;
    final open = lease.isLegalCaseOpen;
    final Color base = open ? const Color(0xFFF59E0B) : Colors.grey;
    final statusLabel = localizeCaseStatus(context, lease.legalCaseStatus);
    final typeLabel = localizeCaseType(context, lease.legalCaseType);

    // Compact form (used on the lease card) — a small chip.
    if (compact) {
      return Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
        decoration: BoxDecoration(
          color: base.withOpacity(0.12),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: base.withOpacity(0.5)),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.balance, size: 13, color: base),
            const SizedBox(width: 4),
            Text('${l.legalCase}: $statusLabel',
                style: TextStyle(
                    fontSize: 11, fontWeight: FontWeight.w600, color: base,),),
          ],
        ),
      );
    }

    // Full banner (lease detail + dashboard).
    final parts = <String>[statusLabel];
    if (typeLabel.isNotEmpty) parts.add(typeLabel);

    return Container(
      width: double.infinity,
      margin: const EdgeInsets.symmetric(vertical: 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: base.withOpacity(0.10),
        borderRadius: BorderRadius.circular(10),
        border: Border(left: BorderSide(color: base, width: 4)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.balance, color: base, size: 20),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('${l.legalCase} · ${parts.join(' · ')}',
                    style: TextStyle(
                        fontWeight: FontWeight.bold,
                        color: base.withOpacity(0.95),),),
                if (lease.legalCaseNextHearing != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    l.legalCaseNextHearing(
                        AppDateUtils.formatLongDate(lease.legalCaseNextHearing),),
                    style: TextStyle(fontSize: 12.5, color: Colors.grey[700]),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}
