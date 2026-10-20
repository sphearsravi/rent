import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/date_utils.dart';
import '../../core/utils/status_localizer.dart';
import '../../data/models/maintenance_model.dart';
import '../../l10n/app_localizations.dart';

/// Maintenance request card widget for dashboard
class MaintenanceCard extends StatelessWidget {
  final MaintenanceModel request;
  final VoidCallback? onTap;

  const MaintenanceCard({
    super.key,
    required this.request,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        margin: const EdgeInsets.only(bottom: 12),
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: AppTheme.backgroundLight,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: AppTheme.borderLight),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header with ID and status
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  '#${request.requestId}',
                  style: Theme.of(context).textTheme.titleSmall?.copyWith(
                        color: AppTheme.primaryIndigo,
                        fontWeight: FontWeight.bold,
                      ),
                ),
                _buildStatusBadge(context),
              ],
            ),
            const SizedBox(height: 12),

            // Category
            Row(
              children: [
                const Icon(Icons.label_outline, size: 16, color: AppTheme.textMuted),
                const SizedBox(width: 6),
                Text(
                  request.category,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: AppTheme.textMuted,
                      ),
                ),
              ],
            ),
            const SizedBox(height: 8),

            // Description
            Text(
              request.description,
              style: Theme.of(context).textTheme.bodyMedium,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
            const SizedBox(height: 12),

            // Date
            Row(
              children: [
                const Icon(Icons.calendar_today, size: 14, color: AppTheme.textMuted),
                const SizedBox(width: 6),
                Text(
                  AppLocalizations.of(context)!.requestedOnLabel(AppDateUtils.formatShortDate(request.requestDate)),
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: AppTheme.textMuted,
                      ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildStatusBadge(BuildContext context) {
    Color badgeColor;
    switch (request.status.toLowerCase()) {
      case 'open':
        badgeColor = AppTheme.infoBlue;
        break;
      case 'in progress':
        badgeColor = AppTheme.warningAmber;
        break;
      case 'completed':
        badgeColor = AppTheme.successGreen;
        break;
      case 'cancelled':
        badgeColor = AppTheme.errorRed;
        break;
      default:
        badgeColor = AppTheme.textMuted;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: badgeColor.withOpacity(0.1),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        localizeStatus(context, request.status),
        style: TextStyle(
          color: badgeColor,
          fontSize: 11,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

/// Detailed maintenance request card
class MaintenanceDetailCard extends StatelessWidget {
  final MaintenanceModel request;

  const MaintenanceDetailCard({
    super.key,
    required this.request,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(12),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.05),
            blurRadius: 8,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Header
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                AppLocalizations.of(context)!.requestNumberShort(request.requestId.toString()),
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.bold,
                    ),
              ),
              _buildStatusBadge(context),
            ],
          ),
          const SizedBox(height: 20),
          const Divider(),
          const SizedBox(height: 16),

          // Details Grid
          _buildDetailRow(context, AppLocalizations.of(context)!.labelProperty, request.propertyName ?? 'N/A'),
          _buildDetailRow(context, AppLocalizations.of(context)!.labelUnit, request.unitNumber ?? 'N/A'),
          _buildDetailRow(context, AppLocalizations.of(context)!.labelCategory, request.category),
          _buildDetailRow(context, AppLocalizations.of(context)!.labelPriority, request.priority),
          _buildDetailRow(context, AppLocalizations.of(context)!.labelRequestDate, AppDateUtils.formatShortDate(request.requestDate)),
          if (request.completedDate != null)
            _buildDetailRow(context, AppLocalizations.of(context)!.labelCompletedDate, AppDateUtils.formatShortDate(request.completedDate)),
          if (request.assignedTo != null)
            _buildDetailRow(context, AppLocalizations.of(context)!.labelAssignedTo, request.assignedTo!),
          const SizedBox(height: 16),

          // Description
          Text(
            AppLocalizations.of(context)!.labelDescription,
            style: Theme.of(context).textTheme.labelLarge?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
          ),
          const SizedBox(height: 8),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: AppTheme.backgroundLight,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Text(
              request.description,
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          ),

          if (request.notes != null) ...[
            const SizedBox(height: 16),
            Text(
              AppLocalizations.of(context)!.labelNotes,
              style: Theme.of(context).textTheme.labelLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
            ),
            const SizedBox(height: 8),
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: AppTheme.backgroundLight,
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text(
                request.notes!,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildStatusBadge(BuildContext context) {
    Color badgeColor;
    switch (request.status.toLowerCase()) {
      case 'open':
        badgeColor = AppTheme.infoBlue;
        break;
      case 'in progress':
        badgeColor = AppTheme.warningAmber;
        break;
      case 'completed':
        badgeColor = AppTheme.successGreen;
        break;
      case 'cancelled':
        badgeColor = AppTheme.errorRed;
        break;
      default:
        badgeColor = AppTheme.textMuted;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: badgeColor.withOpacity(0.1),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(
        localizeStatus(context, request.status),
        style: TextStyle(
          color: badgeColor,
          fontSize: 14,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  Widget _buildDetailRow(BuildContext context, String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 120,
            child: Text(
              label,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: AppTheme.textMuted,
                  ),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w500,
                  ),
            ),
          ),
        ],
      ),
    );
  }
}
