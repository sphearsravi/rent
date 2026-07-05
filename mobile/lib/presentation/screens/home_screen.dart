import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../l10n/app_localizations.dart';
import 'dashboard_screen.dart';
import 'invoices_screen.dart';
import 'maintenance_screen.dart';
import 'profile_screen.dart';

/// Home screen with bottom navigation
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  int _currentIndex = 0;

  // Lazy mount: only the tabs the user has visited are kept alive. This
  // avoids firing every viewmodel's `initState` fetch on app launch, which
  // used to send 5 parallel GETs before the user could see anything.
  final Set<int> _visited = <int>{0};

  // Payments tab removed — payment is now invoice-driven. Each invoice owns
  // its own payment history + receipts (see invoice detail screen). Bottom
  // nav is now Dashboard · Invoices · Maintenance · Profile.
  static const List<Widget> _screens = [
    DashboardScreen(),
    InvoicesScreen(),
    MaintenanceScreen(),
    ProfileScreen(),
  ];

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    return Scaffold(
      body: IndexedStack(
        index: _currentIndex,
        children: List<Widget>.generate(_screens.length, (i) {
          if (!_visited.contains(i)) {
            return const SizedBox.shrink();
          }
          return _screens[i];
        }),
      ),
      bottomNavigationBar: Container(
        decoration: BoxDecoration(
          color: Colors.white,
          boxShadow: [
            BoxShadow(
              color: Colors.black.withOpacity(0.1),
              blurRadius: 20,
              offset: const Offset(0, -5),
            ),
          ],
        ),
        child: SafeArea(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 8),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceAround,
              children: [
                _buildNavItem(0, Icons.dashboard_rounded, l.navDashboard),
                _buildNavItem(1, Icons.receipt_long_rounded, l.navInvoices),
                _buildNavItem(2, Icons.build_rounded, l.navMaintenance),
                _buildNavItem(3, Icons.person_rounded, l.navProfile),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildNavItem(int index, IconData icon, String label) {
    final isSelected = _currentIndex == index;

    return InkWell(
      onTap: () => setState(() {
        _currentIndex = index;
        _visited.add(index);
      }),
      borderRadius: BorderRadius.circular(12),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 200),
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        decoration: BoxDecoration(
          color: isSelected ? AppTheme.primaryIndigo.withOpacity(0.1) : Colors.transparent,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              icon,
              color: isSelected ? AppTheme.primaryIndigo : AppTheme.textMuted,
              size: 24,
            ),
            const SizedBox(height: 4),
            Text(
              label,
              style: TextStyle(
                color: isSelected ? AppTheme.primaryIndigo : AppTheme.textMuted,
                fontSize: 11,
                fontWeight: isSelected ? FontWeight.w600 : FontWeight.w500,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
