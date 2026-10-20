import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../core/utils/date_utils.dart';
import '../../core/utils/status_localizer.dart';
import '../../data/models/lease_model.dart';
import '../../l10n/app_localizations.dart';
import 'legal_case_banner.dart';

/// Property/Lease card widget for dashboard
class PropertyCard extends StatelessWidget {
  final LeaseModel lease;
  final VoidCallback? onTap;

  const PropertyCard({
    super.key,
    required this.lease,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.backgroundLight,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppTheme.borderLight),
      ),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header with property name and status
            Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        lease.propertyName ?? AppLocalizations.of(context)!.labelProperty,
                        style: Theme.of(context).textTheme.titleMedium?.copyWith(
                              fontWeight: FontWeight.bold,
                            ),
                      ),
                      const SizedBox(height: 4),
                      Row(
                        children: [
                          const Icon(Icons.location_on, size: 14, color: AppTheme.textMuted),
                          const SizedBox(width: 4),
                          Expanded(
                            child: Text(
                              lease.fullAddress,
                              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                                    color: AppTheme.textMuted,
                                  ),
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
                _buildStatusBadge(context),
              ],
            ),
            const SizedBox(height: 16),
            const Divider(height: 1),
            const SizedBox(height: 16),

            // Lease Details Grid
            _buildDetailsGrid(context),

            // ⚖️ Legal case chip (renders nothing when no case is filed)
            if (lease.hasLegalCase) ...[
              const SizedBox(height: 12),
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: LegalCaseBanner(lease: lease, compact: true),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _buildStatusBadge(BuildContext context) {
    Color badgeColor;
    String status;

    if (lease.isExpired) {
      badgeColor = AppTheme.errorRed;
      status = AppLocalizations.of(context)!.statusExpired;
    } else if (lease.isExpiring) {
      badgeColor = AppTheme.warningAmber;
      status = AppLocalizations.of(context)!.statusExpiringSoon;
    } else {
      badgeColor = AppTheme.successGreen;
      status = localizeStatus(context, lease.status);
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: badgeColor.withOpacity(0.1),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        status,
        style: TextStyle(
          color: badgeColor,
          fontSize: 12,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  Widget _buildDetailsGrid(BuildContext context) {
    return Wrap(
      spacing: 24,
      runSpacing: 12,
      children: [
        _buildDetailItem(
          context,
          label: AppLocalizations.of(context)!.labelUnitNumber,
          value: lease.unitNumber ?? 'N/A',
        ),
        _buildDetailItem(
          context,
          label: AppLocalizations.of(context)!.labelMonthlyRent,
          value: CurrencyUtils.formatAmount(lease.monthlyRent),
        ),
        _buildDetailItem(
          context,
          label: AppLocalizations.of(context)!.labelSecurityDeposit,
          value: CurrencyUtils.formatAmount(lease.securityDeposit),
        ),
        _buildDetailItem(
          context,
          label: AppLocalizations.of(context)!.labelStartDate,
          value: AppDateUtils.formatShortDate(lease.leaseStartDate),
        ),
        _buildDetailItem(
          context,
          label: AppLocalizations.of(context)!.labelEndDate,
          value: AppDateUtils.formatShortDate(lease.leaseEndDate),
        ),
        _buildDetailItem(
          context,
          label: AppLocalizations.of(context)!.labelDaysRemaining,
          value: AppLocalizations.of(context)!.daysValue(lease.daysRemaining.toString()),
          valueColor: lease.daysRemaining < 30 ? AppTheme.errorRed : null,
        ),
        _buildDetailItem(
          context,
          label: AppLocalizations.of(context)!.labelPaymentDueDay,
          value: AppLocalizations.of(context)!.paymentDueDayValue(lease.paymentDueDay.toString()),
        ),
      ],
    );
  }

  Widget _buildDetailItem(
    BuildContext context, {
    required String label,
    required String value,
    Color? valueColor,
  }) {
    return SizedBox(
      width: 150,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
          const SizedBox(height: 2),
          Text(
            value,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                  color: valueColor,
                ),
          ),
        ],
      ),
    );
  }
}
