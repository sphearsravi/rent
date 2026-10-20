import 'package:flutter/material.dart';
import '../../l10n/app_localizations.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/lease_viewmodel.dart';
import '../widgets/gradient_background.dart';
import '../widgets/property_card.dart';
import '../widgets/legal_case_banner.dart';

/// Screen showing all leases/properties
class LeasesScreen extends StatefulWidget {
  const LeasesScreen({super.key});

  @override
  State<LeasesScreen> createState() => _LeasesScreenState();
}

class _LeasesScreenState extends State<LeasesScreen> {
  @override
  void initState() {
    super.initState();
    // Defer past the first frame: loadLeases() calls notifyListeners()
    // synchronously, which throws "markNeedsBuild called during build" if any
    // other Consumer<LeaseViewModel> is mounted (e.g. the maintenance sheet).
    WidgetsBinding.instance.addPostFrameCallback((_) => _loadData());
  }

  Future<void> _loadData() async {
    final authViewModel = context.read<AuthViewModel>();
    final tenantId = authViewModel.tenantId;
    if (tenantId != null) {
      await context.read<LeaseViewModel>().loadLeases(tenantId);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(AppLocalizations.of(context)!.dashboardMyProperties),
        flexibleSpace: Container(
          decoration: const BoxDecoration(
            gradient: AppTheme.primaryGradient,
          ),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _loadData,
          ),
        ],
      ),
      body: GradientBackground(
        child: Consumer<LeaseViewModel>(
          builder: (context, viewModel, _) {
            if (viewModel.isLoading && !viewModel.hasLeases) {
              return const Center(
                child: CircularProgressIndicator(color: AppTheme.primaryIndigo),
              );
            }

            if (viewModel.state == LeaseState.error && !viewModel.hasLeases) {
              return _buildErrorState(viewModel.errorMessage ?? AppLocalizations.of(context)!.leasesFailedToLoad);
            }

            if (!viewModel.hasLeases) {
              return _buildEmptyState();
            }

            return RefreshIndicator(
              onRefresh: _loadData,
              color: AppTheme.primaryIndigo,
              child: ListView.builder(
                padding: const EdgeInsets.all(16),
                itemCount: viewModel.leases.length,
                itemBuilder: (context, index) {
                  final lease = viewModel.leases[index];
                  return PropertyCard(
                    lease: lease,
                    onTap: () => _showLeaseDetails(lease),
                  );
                },
              ),
            );
          },
        ),
      ),
    );
  }

  void _showLeaseDetails(lease) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => DraggableScrollableSheet(
        initialChildSize: 0.7,
        maxChildSize: 0.9,
        minChildSize: 0.5,
        builder: (context, scrollController) => Container(
          decoration: const BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
          ),
          child: Column(
            children: [
              Container(
                margin: const EdgeInsets.only(top: 12),
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: AppTheme.borderMedium,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
              Expanded(
                child: SingleChildScrollView(
                  controller: scrollController,
                  padding: const EdgeInsets.all(20),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      LegalCaseBanner(lease: lease),
                      PropertyCard(lease: lease),
                    ],
                  ),
                ),
              ),
            ],
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

  Widget _buildEmptyState() {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.home_rounded, size: 80, color: AppTheme.textMuted.withOpacity(0.3)),
          const SizedBox(height: 16),
          Text(
            AppLocalizations.of(context)!.leasesEmptyTitle,
            style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
          const SizedBox(height: 8),
          Text(
            AppLocalizations.of(context)!.leasesEmptyHint,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
        ],
      ),
    );
  }
}
