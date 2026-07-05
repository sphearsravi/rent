import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../core/utils/date_utils.dart';
import '../../core/utils/receipt_launcher.dart';
import '../../core/utils/status_localizer.dart';
import '../../data/models/payment_model.dart';
import '../../l10n/app_localizations.dart';

/// Payment list item widget for showing payment in a list
class PaymentListItem extends StatelessWidget {
  final PaymentModel payment;
  final VoidCallback? onTap;

  const PaymentListItem({
    super.key,
    required this.payment,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(8),
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 12),
        decoration: const BoxDecoration(
          border: Border(bottom: BorderSide(color: AppTheme.borderLight)),
        ),
        child: Row(
          children: [
            // Date
            SizedBox(
              width: 90,
              child: Text(
                AppDateUtils.formatShortDate(payment.paymentDate),
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: AppTheme.textSecondary,
                    ),
              ),
            ),
            // Property
            Expanded(
              child: Text(
                payment.propertyName ?? AppLocalizations.of(context)!.labelProperty,
                style: Theme.of(context).textTheme.bodyMedium,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            // Amount
            SizedBox(
              width: 100,
              child: Text(
                CurrencyUtils.formatAmount(payment.amountPaid, showCurrency: false),
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                textAlign: TextAlign.end,
              ),
            ),
            const SizedBox(width: 12),
            // Status Badge
            _buildStatusBadge(context),
            const SizedBox(width: 8),
            // View Receipt — downloads the PDF from
            // GET /api/payments/{id}/receipt.pdf with the JWT attached.
            if (payment.isCompleted)
              IconButton(
                icon: Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    gradient: AppTheme.primaryGradient,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: const Icon(Icons.receipt_long, color: Colors.white, size: 16),
                ),
                tooltip: AppLocalizations.of(context)!.paymentViewReceipt,
                onPressed: () => ReceiptLauncher.open(context, payment.paymentId),
                padding: EdgeInsets.zero,
                constraints: const BoxConstraints(),
              ),
          ],
        ),
      ),
    );
  }

  Widget _buildStatusBadge(BuildContext context) {
    Color badgeColor;
    switch (payment.paymentStatus.toLowerCase()) {
      case 'completed':
      case 'approved':
        badgeColor = AppTheme.successGreen;
        break;
      case 'pending':
        badgeColor = AppTheme.warningAmber;
        break;
      case 'rejected':
      case 'failed':
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
        localizeStatus(context, payment.paymentStatus),
        style: TextStyle(
          color: badgeColor,
          fontSize: 11,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
