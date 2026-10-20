import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../core/utils/date_utils.dart';
import '../../core/utils/invoice_launcher.dart';
import '../../core/utils/receipt_launcher.dart';
import '../../core/utils/status_localizer.dart';
import '../../l10n/app_localizations.dart';
import '../../data/models/invoice_detail_model.dart';
import '../../data/models/payment_model.dart' show InvoiceModel;
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/invoice_viewmodel.dart';
import '../widgets/gradient_background.dart';

/// Full invoice detail — replaces the old quick-look bottom sheet AND the
/// removed Payments tab. Shows the invoice header, the per-invoice payment
/// history (with receipt-PDF buttons on completed payments), and a Pay-now
/// action that routes into the payment-method flow for THIS invoice.
class InvoiceDetailScreen extends StatefulWidget {
  const InvoiceDetailScreen({
    super.key,
    required this.invoiceId,
    this.oldestUnpaidId,
  });

  final int invoiceId;

  /// The tenant's oldest unpaid invoice id (pay-oldest-first rule). When set
  /// and this invoice isn't the oldest, the Pay button is replaced with a
  /// disabled "pay #X first" notice so the tenant can't hit the 409 dead-end.
  /// null = caller doesn't know the order → Pay is shown (server still guards).
  final int? oldestUnpaidId;

  @override
  State<InvoiceDetailScreen> createState() => _InvoiceDetailScreenState();
}

class _InvoiceDetailScreenState extends State<InvoiceDetailScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final tenantId = context.read<AuthViewModel>().tenantId;
    if (tenantId == null) return;
    await context
        .read<InvoiceViewModel>()
        .loadInvoiceDetails(tenantId, widget.invoiceId);
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    return Scaffold(
      appBar: AppBar(
        title: Text(l.invoiceDetailTitle(widget.invoiceId)),
        flexibleSpace: Container(
          decoration: const BoxDecoration(gradient: AppTheme.primaryGradient),
        ),
      ),
      body: GradientBackground(
        child: Consumer<InvoiceViewModel>(
          builder: (context, vm, _) {
            if (vm.isLoading && vm.selectedInvoice == null) {
              return const Center(
                child: CircularProgressIndicator(color: AppTheme.primaryIndigo),
              );
            }
            if (vm.state == InvoiceState.error && vm.selectedInvoice == null) {
              return _error(vm.errorMessage ?? l.invoiceFailedToLoad);
            }
            final inv = vm.selectedInvoice;
            if (inv == null) return _error(l.invoiceNotFound);
            return RefreshIndicator(
              onRefresh: _load,
              color: AppTheme.primaryIndigo,
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  _summaryCard(inv),
                  const SizedBox(height: 16),
                  _amountsCard(inv),
                  const SizedBox(height: 16),
                  _paymentsSection(inv),
                  const SizedBox(height: 24),
                  if (inv.isOutstanding && _isPayable(inv)) _payNowButton(inv),
                  if (inv.isOutstanding && !_isPayable(inv)) _payBlockedNotice(),
                  if (inv.isOutstanding) const SizedBox(height: 12),
                  _downloadPdfButton(inv),
                ],
              ),
            );
          },
        ),
      ),
    );
  }

  Widget _summaryCard(InvoiceDetailModel inv) {
    final l = AppLocalizations.of(context)!;
    final statusColor = inv.isPaid
        ? AppTheme.successGreen
        : inv.isOverdue
            ? AppTheme.errorRed
            : AppTheme.warningAmber;
    return _card(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  inv.propertyName ?? '—',
                  style: Theme.of(context)
                      .textTheme
                      .titleMedium
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
              ),
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(
                  color: statusColor.withOpacity(0.12),
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Text(
                  localizeStatus(context, inv.status),
                  style: TextStyle(
                    color: statusColor,
                    fontWeight: FontWeight.w700,
                    fontSize: 12,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(l.invoiceUnit(inv.unitNumber ?? '—'),
              style: Theme.of(context)
                  .textTheme
                  .bodySmall
                  ?.copyWith(color: AppTheme.textMuted),),
          const Divider(height: 24),
          _kv(
              l.invoiceTypeLabel,
              (inv.invoiceType == null ||
                      inv.invoiceType!.toLowerCase() == 'rent')
                  ? l.invoiceTypeRent
                  : inv.invoiceType!,),
          _kv(l.invoiceDateLabel,
              AppDateUtils.formatShortDate(inv.invoiceDate),),
          _kv(l.invoiceDueDateLabel,
              AppDateUtils.formatShortDate(inv.dueDate),),
          if ((inv.notes ?? '').isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(inv.notes!,
                style: Theme.of(context)
                    .textTheme
                    .bodySmall
                    ?.copyWith(color: AppTheme.textMuted),),
          ],
        ],
      ),
    );
  }

  Widget _amountsCard(InvoiceDetailModel inv) {
    final l = AppLocalizations.of(context)!;
    return _card(
      child: Column(
        children: [
          _amountRow(l.invoiceTotalLabel,
              CurrencyUtils.formatAmount(inv.totalAmount),
              bold: true,),
          const SizedBox(height: 8),
          _amountRow(l.invoicePaidLabel,
              CurrencyUtils.formatAmount(inv.paidAmount),
              color: AppTheme.successGreen,),
          const SizedBox(height: 8),
          _amountRow(l.invoiceBalanceLabel,
              CurrencyUtils.formatAmount(inv.balanceAmount),
              color: inv.balanceAmount > 0
                  ? AppTheme.errorRed
                  : AppTheme.successGreen,
              bold: true,),
        ],
      ),
    );
  }

  Widget _paymentsSection(InvoiceDetailModel inv) {
    final l = AppLocalizations.of(context)!;
    return _card(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(l.invoicePaymentsLabel,
              style: Theme.of(context)
                  .textTheme
                  .titleMedium
                  ?.copyWith(fontWeight: FontWeight.w700),),
          const SizedBox(height: 12),
          if (inv.payments.isEmpty)
            Text(l.invoiceNoPayments,
                style: Theme.of(context)
                    .textTheme
                    .bodySmall
                    ?.copyWith(color: AppTheme.textMuted),)
          else
            ...inv.payments.map(_paymentRow),
        ],
      ),
    );
  }

  Widget _paymentRow(InvoicePaymentSummary p) {
    // Failed / rejected payments are red (matches the payment-result screen),
    // not amber "pending".
    final st = p.status.toLowerCase();
    final isFailed = st == 'failed' || st == 'rejected' || st == 'cancelled';
    final statusColor = p.isCompleted
        ? AppTheme.successGreen
        : isFailed
            ? AppTheme.errorRed
            : AppTheme.warningAmber;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Row(
        children: [
          Container(
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: statusColor.withOpacity(0.12),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(Icons.payments_rounded,
                size: 18, color: statusColor,),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                    '${CurrencyUtils.formatAmount(p.amountPaid)} • ${localizePaymentType(context, p.paymentType)}',
                    style: const TextStyle(fontWeight: FontWeight.w600),),
                Text(
                  '${AppDateUtils.formatShortDate(p.paymentDate)} • ${localizeStatus(context, p.status)}',
                  style: Theme.of(context)
                      .textTheme
                      .bodySmall
                      ?.copyWith(color: AppTheme.textMuted),
                ),
              ],
            ),
          ),
          if (p.isCompleted)
            IconButton(
              tooltip: AppLocalizations.of(context)!.invoiceReceipt,
              icon: const Icon(Icons.receipt_long_rounded,
                  color: AppTheme.primaryIndigo,),
              onPressed: () => ReceiptLauncher.open(context, p.paymentId),
            ),
        ],
      ),
    );
  }

  Widget _downloadPdfButton(InvoiceDetailModel inv) {
    // tenantId on the invoice model is nullable; the route requires it,
    // so fall back to the logged-in user's tenantId from AuthViewModel
    // (which is the only legit caller anyway).
    final tenantId = inv.tenantId ?? context.read<AuthViewModel>().tenantId;
    return SizedBox(
      width: double.infinity,
      child: OutlinedButton.icon(
        onPressed: tenantId == null
            ? null
            : () => InvoiceLauncher.open(
                  context,
                  tenantId: tenantId,
                  invoiceId: inv.invoiceId,
                ),
        icon: const Icon(Icons.picture_as_pdf_rounded),
        label: Text(AppLocalizations.of(context)!.invoiceDownloadPdf),
        style: OutlinedButton.styleFrom(
          padding: const EdgeInsets.symmetric(vertical: 14),
          foregroundColor: AppTheme.primaryIndigo,
          side: const BorderSide(color: AppTheme.primaryIndigo),
        ),
      ),
    );
  }

  /// True when this invoice may be paid now — i.e. it's the tenant's oldest
  /// unpaid invoice (or the caller didn't specify an order).
  bool _isPayable(InvoiceDetailModel inv) =>
      widget.oldestUnpaidId == null || inv.invoiceId == widget.oldestUnpaidId;

  /// Shown instead of Pay-Now for a locked (not-oldest) outstanding invoice:
  /// a disabled amber notice telling the tenant which invoice to pay first,
  /// so they never reach the 409 error screen.
  Widget _payBlockedNotice() {
    return SizedBox(
      width: double.infinity,
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 16),
        decoration: BoxDecoration(
          color: AppTheme.warningAmber.withOpacity(0.10),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: AppTheme.warningAmber.withOpacity(0.4)),
        ),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.lock_outline, size: 18, color: AppTheme.warningAmber),
            const SizedBox(width: 8),
            Flexible(
              child: Text(
                AppLocalizations.of(context)!
                    .payInvoiceFirst(widget.oldestUnpaidId.toString()),
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: AppTheme.warningAmber,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _payNowButton(InvoiceDetailModel inv) {
    return SizedBox(
      width: double.infinity,
      child: ElevatedButton.icon(
        onPressed: () {
          // Hand the payment-method flow an InvoiceModel built from the
          // detail (the make-payment screen only needs id/amount/property).
          final invoiceForPayment = InvoiceModel(
            invoiceId: inv.invoiceId,
            leaseId: inv.leaseId,
            tenantId: inv.tenantId,
            propertyId: inv.propertyId,
            propertyName: inv.propertyName,
            unitNumber: inv.unitNumber,
            totalAmount: inv.balanceAmount > 0
                ? inv.balanceAmount
                : inv.totalAmount,
            paidAmount: inv.paidAmount,
            balanceAmount: inv.balanceAmount,
            dueDate: inv.dueDate,
            invoiceDate: inv.invoiceDate,
            status: inv.status,
            description: inv.notes,
          );
          Navigator.pushNamed(context, '/payments/make',
              arguments: invoiceForPayment,);
        },
        icon: const Icon(Icons.payment_rounded),
        label: Text(AppLocalizations.of(context)!.invoicePayNow),
        style: ElevatedButton.styleFrom(
          padding: const EdgeInsets.symmetric(vertical: 16),
        ),
      ),
    );
  }

  // ---- small helpers ----

  Widget _card({required Widget child}) => Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: AppTheme.borderMedium),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withOpacity(0.04),
              blurRadius: 12,
              offset: const Offset(0, 4),
            ),
          ],
        ),
        child: child,
      );

  Widget _kv(String k, String v) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(k, style: const TextStyle(color: AppTheme.textMuted)),
            Text(v, style: const TextStyle(fontWeight: FontWeight.w600)),
          ],
        ),
      );

  Widget _amountRow(String k, String v,
      {Color? color, bool bold = false,}) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(k,
            style: TextStyle(
                color: AppTheme.textMuted,
                fontWeight: bold ? FontWeight.w600 : FontWeight.w400,),),
        Text(v,
            style: TextStyle(
                color: color ?? AppTheme.textPrimary,
                fontWeight: bold ? FontWeight.w800 : FontWeight.w600,
                fontSize: bold ? 16 : 14,),),
      ],
    );
  }

  Widget _error(String message) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(Icons.error_outline,
                  size: 56, color: AppTheme.errorRed.withOpacity(0.6),),
              const SizedBox(height: 12),
              Text(message,
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodyMedium,),
              const SizedBox(height: 16),
              ElevatedButton.icon(
                onPressed: _load,
                icon: const Icon(Icons.refresh),
                label: Text(AppLocalizations.of(context)!.retry),
              ),
            ],
          ),
        ),
      );
}
