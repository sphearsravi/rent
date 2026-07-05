import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../l10n/app_localizations.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/payment_viewmodel.dart';
import '../widgets/gradient_background.dart';

/// Screen shown after returning from KNET payment gateway
/// Checks payment status and displays result
class PaymentResultScreen extends StatefulWidget {
  final String? paymentId;
  final String? transactionReference;

  const PaymentResultScreen({
    super.key,
    this.paymentId,
    this.transactionReference,
  });

  @override
  State<PaymentResultScreen> createState() => _PaymentResultScreenState();
}

class _PaymentResultScreenState extends State<PaymentResultScreen> {
  bool _isChecking = true;
  // Empty until the first status resolves; build() shows the localized
  // "checking…" string in the meantime so there's no English flash.
  String _status = '';
  bool? _isSuccess;

  // KNET settlement can lag a few seconds behind the redirect back. Poll a
  // few times automatically before falling back to a manual "Check Again",
  // so the common case resolves without the tenant tapping anything.
  int _attempts = 0;
  static const int _maxAutoAttempts = 3;
  static const Duration _pollInterval = Duration(seconds: 3);

  @override
  void initState() {
    super.initState();
    _checkPaymentStatus();
  }

  Future<void> _checkPaymentStatus() async {
    final l = AppLocalizations.of(context)!;
    final authViewModel = context.read<AuthViewModel>();
    final paymentViewModel = context.read<PaymentViewModel>();

    if (authViewModel.tenantId == null || widget.paymentId == null) {
      setState(() {
        _isChecking = false;
        _status = l.paymentUnableToVerifyShort;
        _isSuccess = null;
      });
      return;
    }

    final paymentIdInt = int.tryParse(widget.paymentId!);
    if (paymentIdInt == null) {
      setState(() {
        _isChecking = false;
        _status = l.paymentInvalidReference;
        _isSuccess = false;
      });
      return;
    }

    _attempts++;
    // First attempt: short grace period for the gateway callback to land.
    await Future.delayed(
        _attempts == 1 ? const Duration(seconds: 2) : _pollInterval);
    if (!mounted) return;

    try {
      await paymentViewModel.loadPaymentDetails(
          authViewModel.tenantId!, paymentIdInt);
      if (!mounted) return;

      final payment = paymentViewModel.selectedPayment;
      if (payment != null && payment.isCompleted) {
        setState(() {
          _isChecking = false;
          _status = l.paymentSuccessful;
          _isSuccess = true;
        });
      } else if (payment != null && payment.isRejected) {
        setState(() {
          _isChecking = false;
          _status = l.paymentResultFailed;
          _isSuccess = false;
        });
      } else if (_attempts < _maxAutoAttempts) {
        // Still pending — keep the spinner up and poll again.
        setState(() {
          _isChecking = true;
          _status = l.paymentProcessing;
          _isSuccess = null;
        });
        _checkPaymentStatus();
      } else {
        setState(() {
          _isChecking = false;
          _status = l.paymentStillProcessing;
          _isSuccess = null;
        });
      }
    } catch (e) {
      if (!mounted) return;
      if (_attempts < _maxAutoAttempts) {
        _checkPaymentStatus();
      } else {
        setState(() {
          _isChecking = false;
          _status = l.paymentUnableToVerify;
          _isSuccess = null;
        });
      }
    }
  }

  void _manualRecheck() {
    setState(() {
      _attempts = 0;
      _isChecking = true;
      _isSuccess = null;
      _status = AppLocalizations.of(context)!.paymentChecking;
    });
    _checkPaymentStatus();
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    return Scaffold(
      appBar: AppBar(
        title: Text(l.paymentStatusScreenTitle),
        flexibleSpace: Container(
          decoration: const BoxDecoration(
            gradient: AppTheme.primaryGradient,
          ),
        ),
      ),
      body: GradientBackground(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(40),
            child: Container(
              constraints: const BoxConstraints(maxWidth: 500),
              padding: const EdgeInsets.all(32),
              decoration: BoxDecoration(
                color: Colors.white,
                borderRadius: BorderRadius.circular(16),
                boxShadow: [
                  BoxShadow(
                    color: Colors.black.withOpacity(0.05),
                    blurRadius: 10,
                    offset: const Offset(0, 4),
                  ),
                ],
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _buildStatusIcon(),
                  const SizedBox(height: 24),
                  Text(
                    _status.isEmpty ? l.paymentChecking : _status,
                    style: TextStyle(
                      fontSize: 22,
                      fontWeight: FontWeight.bold,
                      color: _isSuccess == true
                          ? AppTheme.successGreen
                          : _isSuccess == false
                              ? AppTheme.errorRed
                              : AppTheme.textPrimary,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 16),
                  if (widget.transactionReference != null) ...[
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: AppTheme.backgroundLight,
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(color: AppTheme.borderLight),
                      ),
                      child: Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Text(
                            l.paymentReferenceLabel,
                            style: const TextStyle(
                                color: AppTheme.textMuted, fontSize: 14),
                          ),
                          Text(
                            widget.transactionReference!,
                            style: const TextStyle(
                              fontWeight: FontWeight.w600,
                              fontSize: 14,
                              fontFamily: 'monospace',
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 24),
                  ],
                  // Manual re-check only matters while the payment is still
                  // unresolved and we're not already polling.
                  if (_isSuccess == null && !_isChecking) ...[
                    SizedBox(
                      width: double.infinity,
                      child: OutlinedButton.icon(
                        onPressed: _manualRecheck,
                        icon: const Icon(Icons.refresh),
                        label: Text(l.checkAgain),
                        style: OutlinedButton.styleFrom(
                          padding: const EdgeInsets.symmetric(vertical: 14),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(8),
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                  ],
                  // Single primary action — go back to the app. The old
                  // "View Payment History" button is gone (history now lives
                  // per-invoice) and used to do the exact same navigation.
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton.icon(
                      onPressed: _isChecking
                          ? null
                          : () {
                              Navigator.of(context)
                                  .popUntil((route) => route.isFirst);
                              Navigator.of(context)
                                  .pushReplacementNamed('/home');
                            },
                      icon: const Icon(Icons.home),
                      label: Text(l.done),
                      style: ElevatedButton.styleFrom(
                        backgroundColor: AppTheme.primaryIndigo,
                        foregroundColor: Colors.white,
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(8),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildStatusIcon() {
    if (_isChecking) {
      return Container(
        width: 80,
        height: 80,
        decoration: BoxDecoration(
          color: AppTheme.primaryIndigo.withOpacity(0.1),
          shape: BoxShape.circle,
        ),
        child: const Center(
          child: SizedBox(
            width: 40,
            height: 40,
            child: CircularProgressIndicator(
              strokeWidth: 3,
              valueColor: AlwaysStoppedAnimation<Color>(AppTheme.primaryIndigo),
            ),
          ),
        ),
      );
    }

    if (_isSuccess == true) {
      return Container(
        width: 80,
        height: 80,
        decoration: BoxDecoration(
          color: AppTheme.successGreen.withOpacity(0.1),
          shape: BoxShape.circle,
        ),
        child: const Icon(Icons.check_circle, size: 50, color: AppTheme.successGreen),
      );
    }

    if (_isSuccess == false) {
      return Container(
        width: 80,
        height: 80,
        decoration: BoxDecoration(
          color: AppTheme.errorRed.withOpacity(0.1),
          shape: BoxShape.circle,
        ),
        child: const Icon(Icons.cancel, size: 50, color: AppTheme.errorRed),
      );
    }

    return Container(
      width: 80,
      height: 80,
      decoration: BoxDecoration(
        color: AppTheme.warningAmber.withOpacity(0.1),
        shape: BoxShape.circle,
      ),
      child: const Icon(Icons.hourglass_bottom, size: 50, color: AppTheme.warningAmber),
    );
  }
}
