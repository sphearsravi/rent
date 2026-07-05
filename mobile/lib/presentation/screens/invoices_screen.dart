import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../core/utils/date_utils.dart';
import '../../core/utils/status_localizer.dart';
import '../../l10n/app_localizations.dart';
import '../../data/models/payment_model.dart' show InvoiceModel;
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/invoice_viewmodel.dart';
import '../widgets/gradient_background.dart';
import 'invoice_detail_screen.dart';

/// Invoices tab. Shows every invoice for the current tenant with filter
/// chips for All / Outstanding / Overdue / Paid, pull-to-refresh, and
/// infinite scroll.
///
/// Tapping a row opens [InvoiceDetailScreen] (payments history + receipts +
/// Pay-now); the list refreshes on return so a just-paid invoice updates.
class InvoicesScreen extends StatefulWidget {
  const InvoicesScreen({super.key});

  @override
  State<InvoicesScreen> createState() => _InvoicesScreenState();
}

class _InvoicesScreenState extends State<InvoicesScreen> {
  final ScrollController _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
    // Defer the initial load to after the first frame so the view-model's
    // synchronous notifyListeners() doesn't fire while this widget is
    // still building (the bug we saw with maintenance/dashboard).
    WidgetsBinding.instance.addPostFrameCallback((_) => _loadInitial());
  }

  @override
  void dispose() {
    _scrollController.removeListener(_onScroll);
    _scrollController.dispose();
    super.dispose();
  }

  Future<void> _loadInitial() async {
    final tenantId = context.read<AuthViewModel>().tenantId;
    if (tenantId == null) return;
    await context.read<InvoiceViewModel>().refresh(tenantId);
  }

  Future<void> _refresh() async {
    final tenantId = context.read<AuthViewModel>().tenantId;
    if (tenantId == null) return;
    await context.read<InvoiceViewModel>().refresh(tenantId);
  }

  /// Infinite-scroll trigger: when within 200px of the bottom, fetch next page.
  void _onScroll() {
    if (_scrollController.position.pixels >=
        _scrollController.position.maxScrollExtent - 200) {
      final tenantId = context.read<AuthViewModel>().tenantId;
      if (tenantId != null) {
        context.read<InvoiceViewModel>().loadMore(tenantId);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(AppLocalizations.of(context)!.invoicesTitle),
        flexibleSpace: Container(
          decoration: const BoxDecoration(gradient: AppTheme.primaryGradient),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _refresh,
            tooltip: AppLocalizations.of(context)!.refresh,
          ),
        ],
      ),
      body: GradientBackground(
        child: SafeArea(
          top: false,
          child: Column(
            children: [
              _FilterChips(onChanged: _refresh),
              Expanded(
                child: Consumer<InvoiceViewModel>(
                  builder: (context, vm, _) {
                    if (vm.isLoading && !vm.hasInvoices) {
                      return const Center(
                        child: CircularProgressIndicator(
                          color: AppTheme.primaryIndigo,
                        ),
                      );
                    }

                    if (vm.state == InvoiceState.error && !vm.hasInvoices) {
                      return _ErrorState(
                        message: vm.errorMessage ??
                            AppLocalizations.of(context)!.invoicesFailedToLoad,
                        onRetry: _refresh,
                      );
                    }

                    if (!vm.hasInvoices) {
                      return const _EmptyState();
                    }

                    // Sprint 26 — compute the tenant's oldest unpaid invoice
                    // ID once per build. Anything that isn't this ID gets a
                    // disabled Pay state in the tile + a lock icon. Server
                    // (Sprint 24) enforces the same rule, so this is
                    // belt-and-braces — keeps the UI honest with the API.
                    final unpaid = vm.invoices
                        .where((i) => !i.isPaid && i.status.toLowerCase() != 'cancelled')
                        .toList()
                      ..sort((a, b) {
                        final ad = a.invoiceDate ?? DateTime(2000);
                        final bd = b.invoiceDate ?? DateTime(2000);
                        final cmp = ad.compareTo(bd);
                        return cmp != 0 ? cmp : a.invoiceId.compareTo(b.invoiceId);
                      });
                    final oldestUnpaidId = unpaid.isNotEmpty ? unpaid.first.invoiceId : null;

                    return RefreshIndicator(
                      onRefresh: _refresh,
                      color: AppTheme.primaryIndigo,
                      child: ListView.separated(
                        controller: _scrollController,
                        physics: const AlwaysScrollableScrollPhysics(),
                        padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                        itemCount: vm.invoices.length + (vm.hasMore ? 1 : 0) +
                            (unpaid.length > 1 ? 1 : 0),
                        separatorBuilder: (_, __) => const SizedBox(height: 12),
                        itemBuilder: (context, index) {
                          // Sprint 26 — banner row at the top when 2+ unpaid.
                          if (unpaid.length > 1 && index == 0) {
                            return _PayOldestFirstBanner(
                              oldestId: oldestUnpaidId!,
                              outstandingCount: unpaid.length,
                            );
                          }
                          final invoiceIndex = unpaid.length > 1 ? index - 1 : index;

                          if (invoiceIndex >= vm.invoices.length) {
                            return const Padding(
                              padding: EdgeInsets.symmetric(vertical: 16),
                              child: Center(
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: AppTheme.primaryIndigo,
                                ),
                              ),
                            );
                          }
                          final invoice = vm.invoices[invoiceIndex];
                          final isUnpaid = !invoice.isPaid &&
                              invoice.status.toLowerCase() != 'cancelled';
                          final isPayable = !isUnpaid || invoice.invoiceId == oldestUnpaidId;
                          return _InvoiceTile(
                            invoice: invoice,
                            isPayable: isPayable,
                            oldestUnpaidId: oldestUnpaidId,
                            onTap: () => _openInvoice(invoice, oldestUnpaidId),
                          );
                        },
                      ),
                    );
                  },
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// Open the full invoice detail screen (payments history + receipts +
  /// Pay-now). Refresh the list on return so a just-paid invoice shows its
  /// updated status.
  Future<void> _openInvoice(InvoiceModel invoice, int? oldestUnpaidId) async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => InvoiceDetailScreen(
          invoiceId: invoice.invoiceId,
          oldestUnpaidId: oldestUnpaidId,
        ),
      ),
    );
    if (mounted) _refresh();
  }
}

/// Localized label for an [InvoiceFilter] (the enum's own `label` is English).
String _filterLabel(BuildContext context, InvoiceFilter f) {
  final l = AppLocalizations.of(context)!;
  switch (f) {
    case InvoiceFilter.all:
      return l.invoicesFilterAll;
    case InvoiceFilter.outstanding:
      return l.invoicesFilterOutstanding;
    case InvoiceFilter.overdue:
      return l.invoicesFilterOverdue;
    case InvoiceFilter.paid:
      return l.invoicesFilterPaid;
  }
}

/// Horizontal filter-chip row driven by [InvoiceViewModel.filter].
class _FilterChips extends StatelessWidget {
  const _FilterChips({required this.onChanged});

  final Future<void> Function() onChanged;

  @override
  Widget build(BuildContext context) {
    return Consumer<InvoiceViewModel>(
      builder: (context, vm, _) {
        return SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
          child: Row(
            children: [
              for (final f in InvoiceFilter.values) ...[
                ChoiceChip(
                  label: Text(_filterLabel(context, f)),
                  selected: vm.filter == f,
                  onSelected: (selected) async {
                    if (!selected) return;
                    final tenantId =
                        context.read<AuthViewModel>().tenantId;
                    if (tenantId == null) return;
                    await vm.setFilter(tenantId, f);
                  },
                  selectedColor: AppTheme.primaryIndigo.withOpacity(0.15),
                  labelStyle: TextStyle(
                    color: vm.filter == f
                        ? AppTheme.primaryIndigo
                        : AppTheme.textSecondary,
                    fontWeight:
                        vm.filter == f ? FontWeight.w600 : FontWeight.w500,
                  ),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(20),
                    side: BorderSide(
                      color: vm.filter == f
                          ? AppTheme.primaryIndigo
                          : AppTheme.borderMedium,
                    ),
                  ),
                  backgroundColor: Colors.white,
                ),
                const SizedBox(width: 8),
              ],
            ],
          ),
        );
      },
    );
  }
}

/// Sprint 26 — top-of-list banner explaining the "pay oldest first" rule.
class _PayOldestFirstBanner extends StatelessWidget {
  const _PayOldestFirstBanner({
    required this.oldestId,
    required this.outstandingCount,
  });

  final int oldestId;
  final int outstandingCount;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      margin: const EdgeInsets.only(bottom: 6),
      decoration: BoxDecoration(
        color: AppTheme.primaryIndigo.withOpacity(0.08),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppTheme.primaryIndigo.withOpacity(0.3)),
      ),
      child: Row(
        children: [
          const Icon(Icons.info_outline, color: AppTheme.primaryIndigo, size: 22),
          const SizedBox(width: 10),
          Expanded(
            child: Text.rich(
              TextSpan(
                style: const TextStyle(fontSize: 13, color: AppTheme.textPrimary),
                children: [
                  TextSpan(
                    text: AppLocalizations.of(context)!.payInvoicesInOrder,
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  TextSpan(
                    text: AppLocalizations.of(context)!.startWithInvoiceHint(
                        oldestId.toString(),
                        (outstandingCount - 1).toString()),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Single invoice row.
class _InvoiceTile extends StatelessWidget {
  const _InvoiceTile({
    required this.invoice,
    required this.onTap,
    this.isPayable = true,
    this.oldestUnpaidId,
  });

  final InvoiceModel invoice;
  final VoidCallback onTap;
  /// Sprint 26 — false when this invoice exists but the tenant can't pay
  /// it yet because an older one is still outstanding. Tile is dimmed and
  /// shows a lock icon; tap still opens the detail screen for context.
  final bool isPayable;
  final int? oldestUnpaidId;

  @override
  Widget build(BuildContext context) {
    final isPaid = invoice.isPaid;
    final isOverdue = invoice.isOverdue;
    final statusColor = isPaid
        ? AppTheme.successGreen
        : isOverdue
            ? AppTheme.errorRed
            : AppTheme.warningAmber;

    final dueDateFormatted = AppDateUtils.formatShortDate(invoice.dueDate);
    final amountFormatted =
        CurrencyUtils.formatAmount(invoice.totalAmount, showCurrency: false);

    return Material(
      color: Colors.white,
      borderRadius: BorderRadius.circular(16),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(16),
        child: Opacity(
          opacity: isPayable ? 1.0 : 0.62,
        child: Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
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
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: AppTheme.primaryIndigo.withOpacity(0.10),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: const Icon(
                      Icons.receipt_long_rounded,
                      color: AppTheme.primaryIndigo,
                      size: 22,
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          AppLocalizations.of(context)!
                              .invoiceNumberShort(invoice.invoiceId.toString()),
                          style: Theme.of(context)
                              .textTheme
                              .titleMedium
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          invoice.propertyName ?? '—',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.bodySmall?.copyWith(
                                color: AppTheme.textMuted,
                              ),
                        ),
                      ],
                    ),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 10, vertical: 4),
                    decoration: BoxDecoration(
                      color: statusColor.withOpacity(0.12),
                      borderRadius: BorderRadius.circular(20),
                    ),
                    child: Text(
                      localizeStatus(context, invoice.status),
                      style: TextStyle(
                        color: statusColor,
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 14),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Row(
                    children: [
                      const Icon(Icons.calendar_today_rounded,
                          size: 14, color: AppTheme.textMuted),
                      const SizedBox(width: 6),
                      Text(
                        AppLocalizations.of(context)!
                            .dueColonLabel(dueDateFormatted),
                        style:
                            Theme.of(context).textTheme.bodySmall?.copyWith(
                                  color: AppTheme.textMuted,
                                ),
                      ),
                    ],
                  ),
                  Text(
                    '$amountFormatted KWD',
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                          fontWeight: FontWeight.w800,
                          color: AppTheme.primaryIndigo,
                        ),
                  ),
                ],
              ),
              // Sprint 26 — lock hint shown only on invoices the tenant
              // can't pay yet (older invoice still outstanding).
              if (!isPayable && oldestUnpaidId != null) ...[
                const SizedBox(height: 10),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                  decoration: BoxDecoration(
                    color: AppTheme.warningAmber.withOpacity(0.10),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.lock_outline, size: 14, color: AppTheme.warningAmber),
                      const SizedBox(width: 6),
                      Text(
                        AppLocalizations.of(context)!
                            .payInvoiceFirst(oldestUnpaidId.toString()),
                        style: const TextStyle(
                          fontSize: 11,
                          fontWeight: FontWeight.w600,
                          color: AppTheme.warningAmber,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ],
          ),
        ),
        ),
      ),
    );
  }
}


class _EmptyState extends StatelessWidget {
  const _EmptyState();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.receipt_long_rounded,
            size: 72,
            color: AppTheme.textMuted.withOpacity(0.4),
          ),
          const SizedBox(height: 12),
          Text(
            AppLocalizations.of(context)!.invoicesEmptyTitle,
            style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
          const SizedBox(height: 4),
          Text(
            AppLocalizations.of(context)!.invoicesEmptyHint,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
        ],
      ),
    );
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({required this.message, required this.onRetry});

  final String message;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.error_outline,
                size: 56, color: AppTheme.errorRed.withOpacity(0.6)),
            const SizedBox(height: 12),
            Text(
              AppLocalizations.of(context)!.invoicesErrorTitle,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 6),
            Text(
              message,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: AppTheme.textMuted,
                  ),
            ),
            const SizedBox(height: 16),
            ElevatedButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh),
              label: Text(AppLocalizations.of(context)!.retry),
            ),
          ],
        ),
      ),
    );
  }
}
