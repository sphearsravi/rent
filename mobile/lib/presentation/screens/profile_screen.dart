import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/date_utils.dart';
import '../../core/locale_provider.dart';
import '../../core/constants/app_constants.dart';
import '../../l10n/app_localizations.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/profile_viewmodel.dart';
import '../widgets/gradient_background.dart';
import '../widgets/language_toggle_button.dart';

/// Profile screen showing tenant details
class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  @override
  void initState() {
    super.initState();
    _loadData();
  }

  Future<void> _loadData() async {
    final authViewModel = context.read<AuthViewModel>();
    final tenantId = authViewModel.tenantId;
    if (tenantId != null) {
      await context.read<ProfileViewModel>().loadProfile(tenantId);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    return Scaffold(
      appBar: AppBar(
        title: Text(l.profileTitle),
        flexibleSpace: Container(
          decoration: const BoxDecoration(
            gradient: AppTheme.primaryGradient,
          ),
        ),
        actions: [
          const LanguageToggleButton(),
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: _showLogoutDialog,
            tooltip: l.logout,
          ),
        ],
      ),
      body: GradientBackground(
        child: Consumer<ProfileViewModel>(
          builder: (context, viewModel, _) {
            if (viewModel.isLoading && !viewModel.hasProfile) {
              return const Center(
                child: CircularProgressIndicator(color: AppTheme.primaryIndigo),
              );
            }

            if (viewModel.state == ProfileState.error && !viewModel.hasProfile) {
              return _buildErrorState(
                  viewModel.errorMessage ?? l.profileFailedToLoad);
            }

            return RefreshIndicator(
              onRefresh: _loadData,
              color: AppTheme.primaryIndigo,
              child: SingleChildScrollView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.all(16),
                child: Column(
                  children: [
                    _buildProfileHeader(viewModel),
                    const SizedBox(height: 24),
                    _buildProfileDetails(viewModel),
                    const SizedBox(height: 24),
                    _buildActionButtons(),
                    const SizedBox(height: 100),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }

  Widget _buildProfileHeader(ProfileViewModel viewModel) {
    final profile = viewModel.profile;
    final authViewModel = context.read<AuthViewModel>();

    return Container(
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.05),
            blurRadius: 20,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        children: [
          // Avatar
          Container(
            width: 100,
            height: 100,
            decoration: BoxDecoration(
              gradient: AppTheme.primaryGradient,
              shape: BoxShape.circle,
              boxShadow: [
                BoxShadow(
                  color: AppTheme.primaryIndigo.withOpacity(0.3),
                  blurRadius: 20,
                  offset: const Offset(0, 8),
                ),
              ],
            ),
            child: Center(
              child: Text(
                profile?.initials ?? 'TN',
                style: const TextStyle(
                  color: Colors.white,
                  fontSize: 36,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ),
          const SizedBox(height: 16),
          // Name
          Text(
            profile?.fullName ?? authViewModel.tenantName ?? 'Tenant',
            style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                  fontWeight: FontWeight.bold,
                ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 4),
          // Email
          if (profile?.email != null)
            Text(
              profile!.email!,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: AppTheme.textMuted,
                  ),
            ),
          const SizedBox(height: 8),
          // Status Badge
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            decoration: BoxDecoration(
              color: AppTheme.successGreen.withOpacity(0.1),
              borderRadius: BorderRadius.circular(20),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 8,
                  height: 8,
                  decoration: const BoxDecoration(
                    color: AppTheme.successGreen,
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 8),
                Text(
                  profile?.status ?? AppLocalizations.of(context)!.profileStatusActive,
                  style: const TextStyle(
                    color: AppTheme.successGreen,
                    fontWeight: FontWeight.w600,
                    fontSize: 14,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildProfileDetails(ProfileViewModel viewModel) {
    final profile = viewModel.profile;
    final l = AppLocalizations.of(context)!;
    final na = l.profileNotAvailable;

    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(16),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.05),
            blurRadius: 10,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l.profileSectionPersonal,
            style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.bold,
                ),
          ),
          const SizedBox(height: 20),
          _buildDetailItem(
            icon: Icons.badge_outlined,
            label: l.profileLabelCivilId,
            value: profile?.governmentId ?? na,
          ),
          _buildDetailItem(
            icon: Icons.phone_outlined,
            label: l.profileLabelPhone,
            value: profile?.phone ?? na,
          ),
          _buildDetailItem(
            icon: Icons.email_outlined,
            label: l.profileLabelEmail,
            value: profile?.email ?? na,
          ),
          if (profile?.dateOfBirth != null)
            _buildDetailItem(
              icon: Icons.cake_outlined,
              label: l.profileLabelDob,
              value: AppDateUtils.formatShortDate(profile?.dateOfBirth),
            ),
          if (profile?.company != null)
            _buildDetailItem(
              icon: Icons.business_outlined,
              label: l.profileLabelCompany,
              value: profile?.company ?? na,
            ),
          if (profile?.emergencyContact != null)
            _buildDetailItem(
              icon: Icons.emergency_outlined,
              label: l.profileLabelEmergencyContact,
              value: profile?.emergencyContact ?? na,
            ),
          if (profile?.emergencyPhone != null)
            _buildDetailItem(
              icon: Icons.phone_callback_outlined,
              label: l.profileLabelEmergencyPhone,
              value: profile?.emergencyPhone ?? na,
            ),
        ],
      ),
    );
  }

  Widget _buildDetailItem({
    required IconData icon,
    required String label,
    required String value,
  }) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: AppTheme.primaryIndigo.withOpacity(0.1),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(icon, color: AppTheme.primaryIndigo, size: 20),
          ),
          const SizedBox(width: 16),
          Expanded(
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
                        fontWeight: FontWeight.w500,
                      ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildLanguageTile() {
    return Consumer<LocaleProvider>(
      builder: (context, localeProvider, _) => Container(
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
        child: ListTile(
          onTap: localeProvider.toggle,
          leading: const Icon(Icons.language, color: AppTheme.primaryIndigo),
          title: Text(AppLocalizations.of(context)!.language),
          trailing: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                localeProvider.isArabic ? 'العربية' : 'English',
                style: const TextStyle(
                  color: AppTheme.primaryIndigo,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(width: 4),
              const Icon(Icons.unfold_more,
                  color: AppTheme.textMuted, size: 18),
            ],
          ),
          shape:
              RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        ),
      ),
    );
  }

  Widget _buildActionButtons() {
    return Column(
      children: [
        _buildLanguageTile(),
        const SizedBox(height: 12),
        _buildActionButton(
          icon: Icons.lock_outline,
          label: AppLocalizations.of(context)!.profileActionChangePassword,
          onTap: _showChangePasswordDialog,
        ),
        const SizedBox(height: 12),
        _buildActionButton(
          icon: Icons.help_outline,
          label: AppLocalizations.of(context)!.profileActionHelp,
          onTap: () {
            final msg = AppLocalizations.of(context)!.profileHelpToast;
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(content: Text(msg)),
            );
          },
        ),
        const SizedBox(height: 12),
        _buildActionButton(
          icon: Icons.info_outline,
          label: AppLocalizations.of(context)!.profileActionAbout,
          onTap: () => _showAboutDialog(),
        ),
        const SizedBox(height: 24),
        SizedBox(
          width: double.infinity,
          child: OutlinedButton.icon(
            onPressed: _showLogoutDialog,
            icon: const Icon(Icons.logout, color: AppTheme.errorRed),
            label: Text(AppLocalizations.of(context)!.logout,
                style: const TextStyle(color: AppTheme.errorRed)),
            style: OutlinedButton.styleFrom(
              side: const BorderSide(color: AppTheme.errorRed),
              padding: const EdgeInsets.symmetric(vertical: 16),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildActionButton({
    required IconData icon,
    required String label,
    required VoidCallback onTap,
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
      child: ListTile(
        onTap: onTap,
        leading: Icon(icon, color: AppTheme.primaryIndigo),
        title: Text(label),
        trailing: Icon(
            Directionality.of(context) == TextDirection.rtl
                ? Icons.chevron_left
                : Icons.chevron_right,
            color: AppTheme.textMuted),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      ),
    );
  }

  void _showChangePasswordDialog() {
    final oldPasswordController = TextEditingController();
    final newPasswordController = TextEditingController();
    final confirmPasswordController = TextEditingController();

    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(AppLocalizations.of(context)!.profileActionChangePassword),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: oldPasswordController,
              obscureText: true,
              decoration: InputDecoration(
                labelText: AppLocalizations.of(context)!.changePasswordCurrent,
                prefixIcon: const Icon(Icons.lock_outline),
              ),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: newPasswordController,
              obscureText: true,
              decoration: InputDecoration(
                labelText: AppLocalizations.of(context)!.changePasswordNew,
                prefixIcon: const Icon(Icons.lock_outline),
              ),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: confirmPasswordController,
              obscureText: true,
              decoration: InputDecoration(
                labelText: AppLocalizations.of(context)!.changePasswordConfirm,
                prefixIcon: const Icon(Icons.lock_outline),
              ),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: Text(AppLocalizations.of(context)!.cancel),
          ),
          ElevatedButton(
            onPressed: () async {
              if (newPasswordController.text != confirmPasswordController.text) {
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(
                    content: Text(AppLocalizations.of(context)!.changePasswordMismatch),
                    backgroundColor: AppTheme.errorRed,
                  ),
                );
                return;
              }

              final authViewModel = context.read<AuthViewModel>();
              final success = await authViewModel.changePassword(
                oldPasswordController.text,
                newPasswordController.text,
              );

              if (mounted) {
                Navigator.pop(context);
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(
                    content: Text(success
                        ? AppLocalizations.of(context)!.changePasswordSuccess
                        : AppLocalizations.of(context)!.changePasswordFailed),
                    backgroundColor: success ? AppTheme.successGreen : AppTheme.errorRed,
                  ),
                );
              }
            },
            child: Text(AppLocalizations.of(context)!.changeAction),
          ),
        ],
      ),
    );
  }

  void _showAboutDialog() {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: Row(
          children: [
            ShaderMask(
              shaderCallback: (bounds) => AppTheme.primaryGradient.createShader(bounds),
              child: const Icon(Icons.apartment_rounded, size: 32, color: Colors.white),
            ),
            const SizedBox(width: 12),
            Text(AppLocalizations.of(context)!.appName),
          ],
        ),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(AppLocalizations.of(context)!.aboutVersion(AppConstants.appVersion)),
            const SizedBox(height: 8),
            Text(
              AppLocalizations.of(context)!.aboutTagline,
              style: const TextStyle(color: AppTheme.textMuted),
            ),
            const SizedBox(height: 16),
            Text(
              AppLocalizations.of(context)!.aboutDescription,
              style: const TextStyle(color: AppTheme.textSecondary),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: Text(AppLocalizations.of(context)!.close),
          ),
        ],
      ),
    );
  }

  void _showLogoutDialog() {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(AppLocalizations.of(context)!.logoutConfirmTitle),
        content: Text(AppLocalizations.of(context)!.logoutConfirmMessage),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: Text(AppLocalizations.of(context)!.cancel),
          ),
          ElevatedButton(
            onPressed: () async {
              Navigator.pop(context);
              await context.read<AuthViewModel>().logout();
              if (mounted) {
                Navigator.of(context).pushNamedAndRemoveUntil('/login', (route) => false);
              }
            },
            style: ElevatedButton.styleFrom(
              backgroundColor: AppTheme.errorRed,
            ),
            child: Text(AppLocalizations.of(context)!.logout),
          ),
        ],
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
}
