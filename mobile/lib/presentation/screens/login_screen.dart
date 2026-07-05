import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../l10n/app_localizations.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../widgets/gradient_background.dart';
import '../widgets/language_toggle_button.dart';
import '../widgets/loading_overlay.dart';
import '../../data/services/fcm_service.dart';

/// Login screen matching the tenant portal web design
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> with SingleTickerProviderStateMixin {
  final _formKey = GlobalKey<FormState>();
  final _civilIdController = TextEditingController();
  final _passwordController = TextEditingController();
  bool _rememberMe = false;
  bool _obscurePassword = true;
  late AnimationController _animationController;
  late Animation<double> _fadeAnimation;
  late Animation<Offset> _slideAnimation;

  @override
  void initState() {
    super.initState();
    _animationController = AnimationController(
      duration: const Duration(milliseconds: 500),
      vsync: this,
    );
    _fadeAnimation = Tween<double>(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeOut),
    );
    _slideAnimation = Tween<Offset>(
      begin: const Offset(0, 0.1),
      end: Offset.zero,
    ).animate(CurvedAnimation(parent: _animationController, curve: Curves.easeOut));
    _animationController.forward();
  }

  @override
  void dispose() {
    _civilIdController.dispose();
    _passwordController.dispose();
    _animationController.dispose();
    super.dispose();
  }

  Future<void> _handleLogin() async {
    if (!_formKey.currentState!.validate()) return;

    final authViewModel = context.read<AuthViewModel>();
    final success = await authViewModel.login(
      _civilIdController.text.trim(),
      _passwordController.text,
      rememberMe: _rememberMe,
    );

    if (success && mounted) {
      // Sprint 26 — register this device with the server for push
      // notifications. Fire-and-forget; FcmService catches its own errors
      // so a failed registration never blocks the login redirect.
      final tenantId = authViewModel.tenantId;
      if (tenantId != null) {
        // ignore: discarded_futures
        context.read<FcmService>().registerWithServer(tenantId);
      }
      Navigator.of(context).pushReplacementNamed('/home');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: GradientBackground(
        child: SafeArea(
          child: Consumer<AuthViewModel>(
            builder: (context, authViewModel, _) {
              return LoadingOverlay(
                isLoading: authViewModel.isLoading,
                child: Center(
                  child: SingleChildScrollView(
                    padding: const EdgeInsets.all(24),
                    child: FadeTransition(
                      opacity: _fadeAnimation,
                      child: SlideTransition(
                        position: _slideAnimation,
                        child: _buildLoginCard(authViewModel),
                      ),
                    ),
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }

  Widget _buildLoginCard(AuthViewModel authViewModel) {
    return Container(
      constraints: const BoxConstraints(maxWidth: 400),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.15),
            blurRadius: 30,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      padding: const EdgeInsets.all(32),
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            _buildHeader(),
            const SizedBox(height: 32),
            if (authViewModel.errorMessage != null)
              _buildErrorMessage(authViewModel.errorMessage!),
            _buildCivilIdField(),
            const SizedBox(height: 20),
            _buildPasswordField(),
            const SizedBox(height: 16),
            _buildRememberMe(),
            const SizedBox(height: 24),
            _buildLoginButton(),
            // Demo button is debug-only. kDebugMode is constant-folded out of
            // release builds so the entire block (including credentials) is
            // tree-shaken away — never shipped to a tenant device.
            if (kDebugMode) ...[
              const SizedBox(height: 12),
              _buildDemoButton(),
            ],
            const SizedBox(height: 24),
            _buildFooter(),
          ],
        ),
      ),
    );
  }

  Widget _buildHeader() {
    return Column(
      children: [
        Align(
          alignment: Alignment.centerRight,
          child: LanguageToggleButton(color: AppTheme.primaryIndigo),
        ),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            ShaderMask(
              shaderCallback: (bounds) => AppTheme.primaryGradient.createShader(bounds),
              child: const Icon(
                Icons.apartment_rounded,
                size: 40,
                color: Colors.white,
              ),
            ),
            const SizedBox(width: 12),
            ShaderMask(
              shaderCallback: (bounds) => AppTheme.primaryGradient.createShader(bounds),
              child: const Text(
                'VIIJAR',
                style: TextStyle(
                  fontSize: 32,
                  fontWeight: FontWeight.bold,
                  color: Colors.white,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 16),
        Text(
          AppLocalizations.of(context)!.loginTenantPortal,
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.w600,
              ),
        ),
        const SizedBox(height: 8),
        Text(
          AppLocalizations.of(context)!.loginSubtitle,
          style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: AppTheme.textMuted,
              ),
        ),
      ],
    );
  }

  Widget _buildErrorMessage(String message) {
    return Container(
      margin: const EdgeInsets.only(bottom: 20),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppTheme.errorRed.withOpacity(0.1),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppTheme.errorRed.withOpacity(0.3)),
      ),
      child: Row(
        children: [
          const Icon(Icons.error_outline, color: AppTheme.errorRed, size: 20),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              message,
              style: const TextStyle(color: AppTheme.errorRed, fontSize: 14),
            ),
          ),
          IconButton(
            icon: const Icon(Icons.close, size: 18),
            onPressed: () => context.read<AuthViewModel>().clearError(),
            color: AppTheme.errorRed,
            padding: EdgeInsets.zero,
            constraints: const BoxConstraints(),
          ),
        ],
      ),
    );
  }

  Widget _buildCivilIdField() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(Icons.badge_outlined, size: 18, color: AppTheme.textMuted),
            const SizedBox(width: 8),
            Text(
              AppLocalizations.of(context)!.loginCivilIdLabel,
              style: Theme.of(context).textTheme.labelLarge,
            ),
          ],
        ),
        const SizedBox(height: 8),
        TextFormField(
          controller: _civilIdController,
          // Numeric keyboard — Kuwait Civil ID is 12 digits.
          keyboardType: TextInputType.number,
          decoration: InputDecoration(
            hintText: AppLocalizations.of(context)!.loginCivilIdHint,
          ),
          validator: (value) {
            final l = AppLocalizations.of(context)!;
            if (value == null || value.trim().isEmpty) {
              return l.loginCivilIdRequired;
            }
            // Kuwait Civil ID format: 12 digits, first digit is the
            // birth-century indicator (2 = 1900s, 3 = 2000s).
            final cleaned = value.trim();
            if (!RegExp(r'^[23]\d{11}$').hasMatch(cleaned)) {
              return l.loginCivilIdInvalid;
            }
            return null;
          },
        ),
      ],
    );
  }

  Widget _buildPasswordField() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(Icons.lock_outline, size: 18, color: AppTheme.textMuted),
            const SizedBox(width: 8),
            Text(
              AppLocalizations.of(context)!.loginPasswordLabel,
              style: Theme.of(context).textTheme.labelLarge,
            ),
          ],
        ),
        const SizedBox(height: 8),
        TextFormField(
          controller: _passwordController,
          obscureText: _obscurePassword,
          decoration: InputDecoration(
            hintText: AppLocalizations.of(context)!.loginPasswordHint,
            suffixIcon: IconButton(
              icon: Icon(
                _obscurePassword ? Icons.visibility_off : Icons.visibility,
                color: AppTheme.textMuted,
              ),
              onPressed: () => setState(() => _obscurePassword = !_obscurePassword),
            ),
          ),
          validator: (value) {
            if (value == null || value.isEmpty) {
              return AppLocalizations.of(context)!.loginPasswordRequired;
            }
            return null;
          },
        ),
        // Removed: default password hint. Documenting a credential in the UI
        // turned the entire tenant base into a known account list.
      ],
    );
  }

  Widget _buildRememberMe() {
    return Row(
      children: [
        SizedBox(
          width: 24,
          height: 24,
          child: Checkbox(
            value: _rememberMe,
            onChanged: (value) => setState(() => _rememberMe = value ?? false),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(4)),
            activeColor: AppTheme.primaryIndigo,
          ),
        ),
        const SizedBox(width: 12),
        Row(
          children: [
            Icon(Icons.schedule, size: 16, color: AppTheme.textMuted),
            const SizedBox(width: 6),
            Text(
              AppLocalizations.of(context)!.loginRememberMe,
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          ],
        ),
      ],
    );
  }

  Widget _buildLoginButton() {
    return SizedBox(
      width: double.infinity,
      child: Container(
        decoration: BoxDecoration(
          gradient: AppTheme.primaryGradient,
          borderRadius: BorderRadius.circular(12),
          boxShadow: [
            BoxShadow(
              color: AppTheme.primaryIndigo.withOpacity(0.4),
              blurRadius: 12,
              offset: const Offset(0, 4),
            ),
          ],
        ),
        child: ElevatedButton(
          onPressed: _handleLogin,
          style: ElevatedButton.styleFrom(
            backgroundColor: Colors.transparent,
            shadowColor: Colors.transparent,
            padding: const EdgeInsets.symmetric(vertical: 16),
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Icon(Icons.login, size: 20),
              const SizedBox(width: 8),
              Text(AppLocalizations.of(context)!.loginTitle,
                  style: const TextStyle(
                      fontSize: 16, fontWeight: FontWeight.w600)),
            ],
          ),
        ),
      ),
    );
  }

  // Debug-only: prefill demo credentials. Both this method and the call site
  // are guarded by `kDebugMode` so they never ship in release builds.
  // Civil ID is Fatima Al-Kuwaiti's seeded value — matches the new
  // ^[23]\d{11}$ validator.
  void _fillDemoCredentials() {
    assert(kDebugMode, 'Demo login must never be invoked outside debug builds');
    setState(() {
      _civilIdController.text = '284020200002';
      _passwordController.text = 'Welcome@123';
    });
    _handleLogin();
  }

  Widget _buildDemoButton() {
    return SizedBox(
      width: double.infinity,
      child: OutlinedButton(
        onPressed: _fillDemoCredentials,
        style: OutlinedButton.styleFrom(
          padding: const EdgeInsets.symmetric(vertical: 14),
          side: const BorderSide(color: Color(0xFF10B981), width: 1.5),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        ),
        child: const Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.play_circle_outline, size: 20, color: Color(0xFF10B981)),
            SizedBox(width: 8),
            Text(
              'Demo Login (debug only)',
              style: TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w600,
                color: Color(0xFF10B981),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildFooter() {
    return Column(
      children: [
        Container(
          padding: const EdgeInsets.only(top: 16),
          decoration: const BoxDecoration(
            border: Border(top: BorderSide(color: AppTheme.borderLight)),
          ),
          child: Column(
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const Icon(Icons.help_outline, size: 16, color: AppTheme.infoBlue),
                  const SizedBox(width: 6),
                  Flexible(
                    child: Text(
                      AppLocalizations.of(context)!.loginNeedHelp,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ],
    );
  }
}
