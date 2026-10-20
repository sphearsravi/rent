import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../l10n/app_localizations.dart';
import '../../data/models/dashboard_model.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/dashboard_viewmodel.dart';
import '../widgets/gradient_background.dart';
import '../widgets/language_toggle_button.dart';
import '../widgets/stat_card.dart';
import '../widgets/property_card.dart';
import '../widgets/legal_case_banner.dart';
import '../widgets/maintenance_card.dart';
import 'invoices_screen.dart';
import 'maintenance_screen.dart';
import 'leases_screen.dart';
import 'profile_screen.dart';

/// Dashboard screen matching the tenant portal web design
class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  @override
  void initState() {
    super.initState();
    // Defer to after first frame so the view-model's synchronous
    // notifyListeners() doesn't fire mid-build.
    WidgetsBinding.instance.addPostFrameCallback((_) => _loadData());
  }

  Future<void> _loadData() async {
    final authViewModel = context.read<AuthViewModel>();
    final tenantId = authViewModel.tenantId;
    if (tenantId != null) {
      await context.read<DashboardViewModel>().refresh(tenantId);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: GradientBackground(
        child: SafeArea(
          child: RefreshIndicator(
            onRefresh: _loadData,
            color: AppTheme.primaryIndigo,
            child: Consumer<DashboardViewModel>(
              builder: (context, viewModel, _) {
                if (viewModel.isLoading && !viewModel.hasData) {
                  return const Center(
                    child: CircularProgressIndicator(
                      color: AppTheme.primaryIndigo,
                    ),
                  );
                }

                if (viewModel.state == DashboardState.error && !viewModel.hasData) {
                  return _buildErrorState(viewModel.errorMessage ?? AppLocalizations.of(context)!.dashboardFailedToLoad);
                }

                return _buildContent(viewModel);
              },
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildErrorState(String message) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.error_outline, size: 64, color: AppTheme.errorRed.withOpacity(0.5)),
            const SizedBox(height: 16),
            Text(
              message,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodyLarge?.copyWith(color: AppTheme.textSecondary),
            ),
            const SizedBox(height: 24),
            ElevatedButton.icon(
              onPressed: _loadData,
              icon: const Icon(Icons.refresh),
              label: Text(AppLocalizations.of(context)!.retry),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildContent(DashboardViewModel viewModel) {
    final authViewModel = context.read<AuthViewModel>();
    final dashboard = viewModel.dashboardData;

    return CustomScrollView(
      slivers: [
        // Welcome hero — gradient banner gives the dashboard a clear "above the fold"
        // anchor instead of plain text on a flat background. Greeting reads as a
        // personalised hello card; the avatar circle adds a focal point.
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 20, 20, 4),
            child: _buildWelcomeHero(authViewModel.tenantName),
          ),
        ),

        // ⚖️ Legal case notice — surfaced prominently if any active lease has a
        // case filed on it (renders nothing otherwise).
        ..._buildLegalCaseBanners(dashboard),

        // 💳 Account summary — THE useful block: what you owe + when + Pay now.
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
            child: _buildAccountSummary(viewModel),
          ),
        ),

        // ⚡ Quick actions
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 0),
            child: _buildQuickActions(),
          ),
        ),

        // 🔔 Lease-expiry nudge (renders nothing unless a lease is <30 days out)
        ..._buildLeaseExpiryNudge(viewModel),

        // Statistics Cards
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 0),
            child: _buildStatisticsRow(viewModel),
          ),
        ),

        // My Properties Section
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: _buildPropertiesSection(dashboard),
          ),
        ),

        // (Recent Payments / recent-activity strip removed per request —
        // payment history lives per-invoice under the Invoices tab.)

        // Maintenance Requests Section
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: _buildMaintenanceSection(dashboard),
          ),
        ),

        // Bottom padding
        const SliverToBoxAdapter(
          child: SizedBox(height: 100),
        ),
      ],
    );
  }

  Widget _buildStatisticsRow(DashboardViewModel viewModel) {
    // Row, not Wrap — three cards always fit one row at the widths we've
    // designed for. The StatCard widget self-computes its width from
    // MediaQuery, so don't wrap in Expanded.
    final l = AppLocalizations.of(context)!;
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        StatCard(
          icon: Icons.home_rounded,
          iconColor: AppTheme.primaryIndigo,
          title: l.statActiveLeases,
          value: viewModel.activeLeasesCount.toString(),
        ),
        StatCard(
          icon: Icons.payments_rounded,
          iconColor: AppTheme.successGreen,
          title: l.dashboardStatMonthlyRent,
          value: CurrencyUtils.formatAmount(viewModel.totalMonthlyRent),
        ),
        StatCard(
          icon: Icons.build_rounded,
          iconColor: AppTheme.warningAmber,
          title: l.dashboardStatOpenRequests,
          value: viewModel.pendingMaintenanceCount.toString(),
        ),
      ],
    );
  }

  /// Account-summary hero: the single most useful block on the dashboard —
  /// the outstanding balance, an overdue flag, the next due date, and a
  /// prominent Pay-now button. When nothing is owed it flips to a calm
  /// "all paid up" state so the tenant gets instant reassurance.
  Widget _buildAccountSummary(DashboardViewModel viewModel) {
    final l = AppLocalizations.of(context)!;

    // The balance fetch failed: say so plainly rather than inventing a figure
    // or (worse) claiming "all paid up".
    if (viewModel.outstandingUnknown) {
      return Container(
        width: double.infinity,
        padding: const EdgeInsets.all(20),
        decoration: BoxDecoration(
          color: AppTheme.warningAmber.withOpacity(0.10),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: AppTheme.warningAmber.withOpacity(0.35)),
        ),
        child: Row(
          children: [
            const Icon(Icons.cloud_off_rounded, color: AppTheme.warningAmber, size: 36),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(l.dashboardBalanceUnavailable,
                      style: const TextStyle(
                          fontSize: 16, fontWeight: FontWeight.w700, color: AppTheme.textPrimary,),),
                  const SizedBox(height: 4),
                  Text(l.dashboardBalanceUnavailableBody,
                      style: const TextStyle(fontSize: 13, color: AppTheme.textSecondary),),
                ],
              ),
            ),
          ],
        ),
      );
    }

    if (viewModel.isAllPaidUp) {
      return Container(
        width: double.infinity,
        padding: const EdgeInsets.all(20),
        decoration: BoxDecoration(
          color: AppTheme.successGreen.withOpacity(0.10),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: AppTheme.successGreen.withOpacity(0.35)),
        ),
        child: Row(
          children: [
            const Icon(Icons.verified_rounded,
                color: AppTheme.successGreen, size: 40,),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(l.dashboardAllPaidUp,
                      style: const TextStyle(
                          fontSize: 18,
                          fontWeight: FontWeight.w700,
                          color: AppTheme.successGreen,),),
                  const SizedBox(height: 4),
                  Text(l.dashboardAllPaidUpBody,
                      style: const TextStyle(
                          fontSize: 13, color: AppTheme.textSecondary,),),
                ],
              ),
            ),
          ],
        ),
      );
    }

    final overdue = viewModel.overdueCount > 0;
    final days = viewModel.daysUntilNextDue;
    final count = viewModel.outstandingInvoices.length;

    final String dueLine;
    if (days == null) {
      dueLine = '';
    } else if (days < 0) {
      dueLine = l.dashboardOverdueByDays(-days);
    } else if (days == 0) {
      dueLine = l.dashboardDueToday;
    } else {
      dueLine = l.dashboardDueInDays(days);
    }

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        gradient: AppTheme.primaryGradient,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [
          BoxShadow(
            color: AppTheme.primaryIndigo.withOpacity(0.25),
            blurRadius: 24,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(l.dashboardAmountDue,
                  style: const TextStyle(
                      color: Colors.white70,
                      fontSize: 13,
                      fontWeight: FontWeight.w500,),),
              const Spacer(),
              if (overdue)
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                  decoration: BoxDecoration(
                    color: AppTheme.errorRed,
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.warning_amber_rounded,
                          size: 14, color: Colors.white,),
                      const SizedBox(width: 4),
                      Text(l.dashboardOverdueChip(viewModel.overdueCount),
                          style: const TextStyle(
                              color: Colors.white,
                              fontSize: 12,
                              fontWeight: FontWeight.w700,),),
                    ],
                  ),
                ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            CurrencyUtils.formatAmount(viewModel.totalOutstanding),
            style: const TextStyle(
                color: Colors.white,
                fontSize: 34,
                fontWeight: FontWeight.w800,
                letterSpacing: 0.5,),
          ),
          const SizedBox(height: 4),
          Row(
            children: [
              const Icon(Icons.event_rounded, size: 15, color: Colors.white70),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  // Only add the separator when there's a due-line to join.
                  [
                    count > 1 ? l.dashboardInvoicesDueMany(count) : l.dashboardInvoicesDueOne,
                    if (dueLine.isNotEmpty) dueLine,
                  ].join(' · '),
                  style: const TextStyle(color: Colors.white70, fontSize: 13),
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          SizedBox(
            width: double.infinity,
            child: ElevatedButton.icon(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(builder: (_) => const InvoicesScreen()),
              ),
              icon: const Icon(Icons.credit_card_rounded, size: 20),
              label: Text(l.dashboardPayNow,
                  style: const TextStyle(
                      fontSize: 16, fontWeight: FontWeight.w700,),),
              style: ElevatedButton.styleFrom(
                backgroundColor: Colors.white,
                foregroundColor: AppTheme.primaryIndigo,
                padding: const EdgeInsets.symmetric(vertical: 14),
                shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(12),),
              ),
            ),
          ),
        ],
      ),
    );
  }

  /// Four one-tap shortcuts to the things a tenant does most.
  Widget _buildQuickActions() {
    final l = AppLocalizations.of(context)!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(l.dashboardQuickActions,
            style: Theme.of(context)
                .textTheme
                .titleSmall
                ?.copyWith(fontWeight: FontWeight.w700),),
        const SizedBox(height: 12),
        Row(
          children: [
            _quickAction(Icons.credit_card_rounded, l.dashboardActionPay,
                AppTheme.primaryIndigo, () {
              Navigator.of(context).push(
                  MaterialPageRoute(builder: (_) => const InvoicesScreen()),);
            }),
            _quickAction(Icons.add_circle_outline_rounded,
                l.dashboardActionRequest, AppTheme.warningAmber, () {
              showCreateMaintenanceRequestSheet(context);
            }),
            _quickAction(Icons.description_outlined, l.dashboardActionLeases,
                AppTheme.infoBlue, () {
              Navigator.of(context).push(
                  MaterialPageRoute(builder: (_) => const LeasesScreen()),);
            }),
            _quickAction(Icons.person_outline_rounded, l.dashboardActionProfile,
                AppTheme.successGreen, () {
              Navigator.of(context).push(
                  MaterialPageRoute(builder: (_) => const ProfileScreen()),);
            }),
          ],
        ),
      ],
    );
  }

  Widget _quickAction(
      IconData icon, String label, Color color, VoidCallback onTap,) {
    return Expanded(
      child: GestureDetector(
        onTap: onTap,
        behavior: HitTestBehavior.opaque,
        child: Column(
          children: [
            Container(
              width: 52,
              height: 52,
              decoration: BoxDecoration(
                color: color.withOpacity(0.12),
                borderRadius: BorderRadius.circular(16),
              ),
              child: Icon(icon, color: color, size: 24),
            ),
            const SizedBox(height: 6),
            Text(
              label,
              textAlign: TextAlign.center,
              maxLines: 2,
              style: const TextStyle(
                  fontSize: 11,
                  height: 1.2,
                  color: AppTheme.textSecondary,
                  fontWeight: FontWeight.w600,),
            ),
          ],
        ),
      ),
    );
  }

  /// Amber nudge when an active lease is within 30 days of expiry.
  List<Widget> _buildLeaseExpiryNudge(DashboardViewModel viewModel) {
    final lease = viewModel.expiringLease;
    if (lease == null) return const [];
    final l = AppLocalizations.of(context)!;
    return [
      SliverToBoxAdapter(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 16, 20, 0),
          // Tappable: takes the tenant to their leases to act on the nudge.
          child: InkWell(
            onTap: () => Navigator.of(context).push(
              MaterialPageRoute(builder: (_) => const LeasesScreen()),
            ),
            borderRadius: BorderRadius.circular(14),
            child: Container(
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              color: AppTheme.warningAmber.withOpacity(0.10),
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: AppTheme.warningAmber.withOpacity(0.35)),
            ),
            child: Row(
              children: [
                const Icon(Icons.schedule_rounded,
                    color: AppTheme.warningAmber, size: 22,),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(l.dashboardLeaseExpiring(lease.daysRemaining),
                          style: const TextStyle(
                              fontWeight: FontWeight.w700,
                              color: AppTheme.textPrimary,
                              fontSize: 14,),),
                      Text(
                          '${lease.displayName} · ${l.dashboardLeaseExpiringAction}',
                          style: const TextStyle(
                              fontSize: 12, color: AppTheme.textSecondary,),),
                    ],
                  ),
                ),
                const Icon(Icons.chevron_right_rounded, color: AppTheme.textMuted),
              ],
            ),
            ),
          ),
        ),
      ),
    ];
  }

  /// Personalised gradient welcome card. Replaces the plain "Welcome, X!" text.
  Widget _buildWelcomeHero(String? tenantName) {
    final firstName = (tenantName ?? '').split(' ').first;
    final initial = firstName.isNotEmpty ? firstName[0].toUpperCase() : '?';

    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        gradient: AppTheme.primaryGradient,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [
          BoxShadow(
            color: AppTheme.primaryIndigo.withOpacity(0.25),
            blurRadius: 24,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      child: Row(
        children: [
          // Avatar circle with tenant's initial
          Container(
            width: 56,
            height: 56,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: Colors.white.withOpacity(0.20),
              shape: BoxShape.circle,
              border: Border.all(color: Colors.white.withOpacity(0.40), width: 1.5),
            ),
            child: Text(
              initial,
              style: const TextStyle(
                color: Colors.white,
                fontSize: 24,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  _greetingForNow(),
                  style: const TextStyle(
                    color: Colors.white70,
                    fontSize: 13,
                    fontWeight: FontWeight.w500,
                    letterSpacing: 0.2,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  tenantName ?? AppLocalizations.of(context)!.dashboardTenantFallback,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 20,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 6),
                Text(
                  AppLocalizations.of(context)!.dashboardSubtitle,
                  style: TextStyle(
                    color: Colors.white.withOpacity(0.85),
                    fontSize: 13,
                  ),
                ),
              ],
            ),
          ),
          const LanguageToggleButton(),
        ],
      ),
    );
  }

  /// Time-of-day appropriate greeting. Keeps the hero feeling alive instead of
  /// always saying "Welcome".
  String _greetingForNow() {
    final l = AppLocalizations.of(context)!;
    final hour = DateTime.now().hour;
    if (hour < 12) return l.greetingMorning;
    if (hour < 17) return l.greetingAfternoon;
    return l.greetingEvening;
  }

  /// One banner per active lease that has a legal case filed on it. Returned as
  /// slivers so it slots into the dashboard's CustomScrollView.
  List<Widget> _buildLegalCaseBanners(DashboardModel? dashboard) {
    final withCases =
        (dashboard?.activeLeases ?? []).where((l) => l.hasLegalCase).toList();
    if (withCases.isEmpty) return const [];
    return [
      SliverToBoxAdapter(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 4, 20, 0),
          child: Column(
            children:
                withCases.map((l) => LegalCaseBanner(lease: l)).toList(),
          ),
        ),
      ),
    ];
  }

  Widget _buildPropertiesSection(DashboardModel? dashboard) {
    final leases = dashboard?.activeLeases ?? [];

    // Payment is invoice-driven now — there is no standalone "make payment"
    // screen to push without an invoice. Send the tenant to Invoices, where
    // each invoice has its own Pay-now action.
    final l = AppLocalizations.of(context)!;
    return _buildSectionCard(
      title: l.dashboardMyProperties,
      icon: Icons.apartment_rounded,
      actionLabel: l.dashboardViewInvoices,
      onAction: () => Navigator.of(context).push(
        MaterialPageRoute(builder: (_) => const InvoicesScreen()),
      ),
      child: leases.isEmpty
          ? _buildEmptyState(Icons.home_rounded, l.dashboardNoLeases)
          : Column(
              children: leases.map((lease) => PropertyCard(lease: lease)).toList(),
            ),
    );
  }

  Widget _buildMaintenanceSection(DashboardModel? dashboard) {
    final requests = dashboard?.maintenanceRequests ?? [];

    final l = AppLocalizations.of(context)!;
    return _buildSectionCard(
      title: l.dashboardMaintenanceTitle,
      icon: Icons.build_rounded,
      actionLabel: l.dashboardNewRequest,
      onAction: () => showCreateMaintenanceRequestSheet(context),
      child: requests.isEmpty
          ? _buildEmptyState(Icons.build_rounded, l.dashboardNoMaintenance)
          : Column(
              children: requests.map((request) => MaintenanceCard(request: request)).toList(),
            ),
    );
  }

  Widget _buildSectionCard({
    required String title,
    required IconData icon,
    required String actionLabel,
    required VoidCallback onAction,
    required Widget child,
  }) {
    return Container(
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
        children: [
          // Header
          Container(
            padding: const EdgeInsets.all(16),
            decoration: const BoxDecoration(
              border: Border(bottom: BorderSide(color: AppTheme.borderLight)),
            ),
            child: Row(
              children: [
                Icon(icon, color: AppTheme.primaryIndigo, size: 24),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    title,
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                          fontWeight: FontWeight.bold,
                        ),
                  ),
                ),
                TextButton.icon(
                  onPressed: onAction,
                  icon: const Icon(Icons.add, size: 18),
                  label: Text(actionLabel),
                  style: TextButton.styleFrom(
                    foregroundColor: AppTheme.primaryIndigo,
                    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                  ),
                ),
              ],
            ),
          ),
          // Content
          Padding(
            padding: const EdgeInsets.all(16),
            child: child,
          ),
        ],
      ),
    );
  }

  Widget _buildEmptyState(IconData icon, String message) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 32),
      child: Column(
        children: [
          Icon(icon, size: 48, color: AppTheme.textMuted.withOpacity(0.5)),
          const SizedBox(height: 12),
          Text(
            message,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
        ],
      ),
    );
  }
}
