import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../core/utils/date_utils.dart';
import '../../l10n/app_localizations.dart';
import '../../data/models/dashboard_model.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/dashboard_viewmodel.dart';
import '../widgets/gradient_background.dart';
import '../widgets/language_toggle_button.dart';
import '../widgets/stat_card.dart';
import '../widgets/property_card.dart';
import '../widgets/payment_list_item.dart';
import '../widgets/maintenance_card.dart';
import 'invoices_screen.dart';
import 'maintenance_screen.dart';

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

        // Statistics Cards
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
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

        // Recent Payments Section
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            child: _buildRecentPaymentsSection(dashboard),
          ),
        ),

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
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        StatCard(
          icon: Icons.home_rounded,
          iconColor: AppTheme.primaryIndigo,
          title: AppLocalizations.of(context)!.statActiveLeases,
          value: viewModel.activeLeasesCount.toString(),
        ),
        StatCard(
          icon: Icons.check_circle_rounded,
          iconColor: AppTheme.successGreen,
          title: AppLocalizations.of(context)!.statTotalPayments,
          value: viewModel.totalPaymentsCount.toString(),
        ),
        StatCard(
          icon: Icons.build_rounded,
          iconColor: AppTheme.warningAmber,
          title: AppLocalizations.of(context)!.statPendingRequests,
          value: viewModel.pendingMaintenanceCount.toString(),
        ),
      ],
    );
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

  Widget _buildRecentPaymentsSection(DashboardModel? dashboard) {
    final payments = dashboard?.recentPayments ?? [];

    // Read-only recent-activity strip. The dedicated Payments tab/screen was
    // removed — payment history now lives per-invoice. "View All" opens the
    // Invoices screen so the tenant can drill into any invoice's payments.
    final l = AppLocalizations.of(context)!;
    return _buildSectionCard(
      title: l.dashboardRecentPayments,
      icon: Icons.receipt_long_rounded,
      actionLabel: l.dashboardViewInvoices,
      onAction: () => Navigator.of(context).push(
        MaterialPageRoute(builder: (_) => const InvoicesScreen()),
      ),
      child: payments.isEmpty
          ? _buildEmptyState(Icons.receipt_rounded, l.dashboardNoPayments)
          : Column(
              children: payments
                  .take(5)
                  .map((payment) => PaymentListItem(payment: payment))
                  .toList(),
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
