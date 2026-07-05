import 'dart:io' show File;

import 'package:flutter/material.dart';
import '../../l10n/app_localizations.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../core/utils/date_utils.dart';
import '../../core/utils/platform_url_launcher.dart' as platform_launcher;
import '../../core/utils/status_localizer.dart';
import '../../data/models/payment_model.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/payment_viewmodel.dart';
import '../widgets/gradient_background.dart';
import '../widgets/loading_overlay.dart';

/// Make Payment Screen - Select invoice and payment method
class MakePaymentScreen extends StatefulWidget {
  const MakePaymentScreen({super.key});

  @override
  State<MakePaymentScreen> createState() => _MakePaymentScreenState();
}

class _MakePaymentScreenState extends State<MakePaymentScreen> {
  InvoiceModel? _selectedInvoice;
  // When the screen is opened from an invoice ("Pay now"), the invoice is
  // passed as a route argument and we go straight to the payment-method
  // sheet — the tenant never re-picks an invoice.
  InvoiceModel? _invoiceArg;
  bool _argHandled = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_argHandled) return;
    _argHandled = true;

    final arg = ModalRoute.of(context)?.settings.arguments;
    if (arg is InvoiceModel) {
      // Invoice-driven path: we already have the invoice. Do NOT call the
      // pending-invoices endpoint — its failure would block this screen
      // with a full-page error even though the payment sheet works fine.
      _invoiceArg = arg;
      _selectedInvoice = arg;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _showPaymentMethodModal(arg);
      });
    } else {
      // Legacy path: no invoice passed — load the pending list to pick one.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _loadPendingInvoices();
      });
    }
  }

  Future<void> _loadPendingInvoices() async {
    final authViewModel = context.read<AuthViewModel>();
    final paymentViewModel = context.read<PaymentViewModel>();

    if (authViewModel.tenantId != null) {
      paymentViewModel.setTenantId(authViewModel.tenantId!);
      await paymentViewModel.loadPendingInvoices();
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(AppLocalizations.of(context)!.makePayment),
        flexibleSpace: Container(
          decoration: const BoxDecoration(
            gradient: AppTheme.primaryGradient,
          ),
        ),
      ),
      body: GradientBackground(
        child: Consumer<PaymentViewModel>(
          builder: (context, viewModel, child) {
            return LoadingOverlay(
              isLoading: viewModel.isLoading,
              child: RefreshIndicator(
                onRefresh: _loadPendingInvoices,
                child: _buildContent(viewModel),
              ),
            );
          },
        ),
      ),
    );
  }

  Widget _buildContent(PaymentViewModel viewModel) {
    // Invoice-driven entry (opened from an invoice's "Pay now"): we already
    // have the invoice and immediately show the payment-method sheet as a
    // modal, then redirect to the gateway. The pending-invoices list is never
    // loaded here, so DON'T render the list / "No Pending Invoices" empty
    // state — it would flash behind the sheet and on the way to KNET. Show a
    // neutral spinner instead.
    if (_invoiceArg != null) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(24),
          child: CircularProgressIndicator(),
        ),
      );
    }

    if (viewModel.error != null) {
      return _buildErrorState(viewModel.error!);
    }

    final pendingInvoices = viewModel.pendingInvoices;

    if (pendingInvoices.isEmpty && !viewModel.isLoading) {
      return _buildEmptyState();
    }

    return SingleChildScrollView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Header
          _buildSectionHeader(AppLocalizations.of(context)!.paymentSelectInvoice),
          const SizedBox(height: 16),

          // Invoice Cards Grid
          ListView.builder(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            itemCount: pendingInvoices.length,
            itemBuilder: (context, index) {
              final invoice = pendingInvoices[index];
              return _buildInvoiceCard(invoice);
            },
          ),
        ],
      ),
    );
  }

  Widget _buildSectionHeader(String title) {
    return Row(
      children: [
        Container(
          width: 4,
          height: 24,
          decoration: BoxDecoration(
            gradient: AppTheme.primaryGradient,
            borderRadius: BorderRadius.circular(2),
          ),
        ),
        const SizedBox(width: 12),
        Text(
          title,
          style: const TextStyle(
            fontSize: 18,
            fontWeight: FontWeight.bold,
            color: AppTheme.textPrimary,
          ),
        ),
      ],
    );
  }

  Widget _buildInvoiceCard(InvoiceModel invoice) {
    final isOverdue = invoice.status.toLowerCase() == 'overdue';

    return Container(
      margin: const EdgeInsets.only(bottom: 16),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(12),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.05),
            blurRadius: 10,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        children: [
          // Invoice Header
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              border: Border(
                bottom: BorderSide(color: AppTheme.borderLight, width: 2),
              ),
            ),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  AppLocalizations.of(context)!.invoiceNumberShort(invoice.invoiceId.toString()),
                  style: const TextStyle(
                    fontWeight: FontWeight.bold,
                    fontSize: 16,
                    color: AppTheme.textPrimary,
                  ),
                ),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                  decoration: BoxDecoration(
                    color: isOverdue
                        ? AppTheme.errorRed.withOpacity(0.1)
                        : AppTheme.warningAmber.withOpacity(0.1),
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: Text(
                    localizeStatus(context, invoice.status),
                    style: TextStyle(
                      color: isOverdue ? AppTheme.errorRed : AppTheme.warningAmber,
                      fontSize: 12,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ],
            ),
          ),

          // Invoice Body
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              children: [
                // Property Info
                _buildInvoiceDetail(
                  Icons.apartment,
                  '${invoice.propertyName ?? AppLocalizations.of(context)!.labelProperty} - ${AppLocalizations.of(context)!.labelUnit} ${invoice.unitNumber ?? AppLocalizations.of(context)!.notAvailable}',
                ),
                const SizedBox(height: 8),

                // Due Date
                _buildInvoiceDetail(
                  Icons.calendar_today,
                  AppLocalizations.of(context)!.dueColonLabel(AppDateUtils.formatShortDate(invoice.dueDate)),
                ),
                const SizedBox(height: 16),

                // Amount
                Text(
                  CurrencyUtils.formatAmount(invoice.totalAmount),
                  style: const TextStyle(
                    fontSize: 28,
                    fontWeight: FontWeight.bold,
                    color: AppTheme.primaryIndigo,
                  ),
                ),
                const SizedBox(height: 16),

                // Select & Pay Button
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton.icon(
                    onPressed: () => _showPaymentMethodModal(invoice),
                    icon: const Icon(Icons.check_circle),
                    label: Text(AppLocalizations.of(context)!.paymentSelectAndPay),
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
        ],
      ),
    );
  }

  Widget _buildInvoiceDetail(IconData icon, String text) {
    return Row(
      children: [
        Icon(icon, size: 16, color: AppTheme.textMuted),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            text,
            style: TextStyle(
              color: AppTheme.textMuted,
              fontSize: 14,
            ),
          ),
        ),
      ],
    );
  }

  void _showPaymentMethodModal(InvoiceModel invoice) {
    setState(() {
      _selectedInvoice = invoice;
    });

    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => _buildPaymentMethodSheet(invoice),
    );
  }

  Widget _buildPaymentMethodSheet(InvoiceModel invoice) {
    // Cap the sheet at 88% of screen height and make the body scrollable —
    // otherwise on shorter phones the Bank Transfer + Cash cards render
    // below the fold with no way to reach them.
    final maxHeight = MediaQuery.of(context).size.height * 0.88;
    return Container(
      constraints: BoxConstraints(maxHeight: maxHeight),
      decoration: const BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          // Header
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(20),
            decoration: const BoxDecoration(
              gradient: AppTheme.primaryGradient,
              borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
            ),
            child: Column(
              children: [
                // Drag handle
                Container(
                  width: 40,
                  height: 4,
                  margin: const EdgeInsets.only(bottom: 16),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(0.5),
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
                Row(
                  children: [
                    const Icon(Icons.credit_card, color: Colors.white),
                    const SizedBox(width: 12),
                    Text(
                      AppLocalizations.of(context)!.paymentChooseMethod,
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 18,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),

          // Everything below the header scrolls so no payment method is
          // ever clipped on shorter devices.
          Flexible(
            child: SingleChildScrollView(
              child: Column(
                children: [
                  // Selected Invoice Info
                  Container(
                    margin: const EdgeInsets.all(20),
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: AppTheme.primaryIndigo.withOpacity(0.1),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text(
                          AppLocalizations.of(context)!.paymentInvoiceAmount,
                          style: const TextStyle(
                            fontWeight: FontWeight.w500,
                            color: AppTheme.textPrimary,
                          ),
                        ),
                        Text(
                          CurrencyUtils.formatAmount(invoice.totalAmount),
                          style: const TextStyle(
                            fontWeight: FontWeight.bold,
                            fontSize: 18,
                            color: AppTheme.primaryIndigo,
                          ),
                        ),
                      ],
                    ),
                  ),

                  // Payment Methods
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 20),
                    child: Column(
                      children: [
                        // KNET Payment
                        _buildPaymentMethodCard(
                          icon: Icons.credit_card,
                          iconColor: AppTheme.primaryIndigo,
                          title: 'KNET',
                          subtitle: AppLocalizations.of(context)!.paymentKnetSubtitle,
                          buttonText: AppLocalizations.of(context)!.paymentPayNow,
                          buttonColor: AppTheme.primaryIndigo,
                          onTap: () => _payWithKNET(invoice),
                        ),
                        const SizedBox(height: 12),

                        // Bank Transfer
                        _buildPaymentMethodCard(
                          icon: Icons.account_balance,
                          iconColor: AppTheme.successGreen,
                          title: AppLocalizations.of(context)!.paymentBankTransfer,
                          subtitle: AppLocalizations.of(context)!.paymentBankTransferSubtitle,
                          buttonText: AppLocalizations.of(context)!.paymentUploadReceipt,
                          buttonColor: AppTheme.successGreen,
                          onTap: () {
                            Navigator.pop(context);
                            _showBankTransferDialog(invoice);
                          },
                        ),
                        const SizedBox(height: 12),

                        // Cash Payment Info
                        _buildCashPaymentInfo(invoice),
                      ],
                    ),
                  ),

                  const SizedBox(height: 30),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildPaymentMethodCard({
    required IconData icon,
    required Color iconColor,
    required String title,
    required String subtitle,
    required String buttonText,
    required Color buttonColor,
    required VoidCallback onTap,
  }) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppTheme.borderLight, width: 2),
      ),
      child: Row(
        children: [
          // Icon
          Container(
            width: 60,
            height: 60,
            decoration: BoxDecoration(
              color: iconColor.withOpacity(0.1),
              shape: BoxShape.circle,
            ),
            child: Icon(icon, color: iconColor, size: 28),
          ),
          const SizedBox(width: 16),

          // Text Content
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(
                    fontWeight: FontWeight.bold,
                    fontSize: 16,
                    color: AppTheme.textPrimary,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  subtitle,
                  style: TextStyle(
                    fontSize: 13,
                    color: AppTheme.textMuted,
                  ),
                ),
              ],
            ),
          ),

          // Button
          ElevatedButton(
            onPressed: onTap,
            style: ElevatedButton.styleFrom(
              backgroundColor: buttonColor,
              foregroundColor: Colors.white,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
            ),
            child: Text(
              buttonText,
              style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildCashPaymentInfo(InvoiceModel invoice) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppTheme.borderLight, width: 2),
      ),
      child: Row(
        children: [
          // Icon
          Container(
            width: 60,
            height: 60,
            decoration: BoxDecoration(
              color: AppTheme.warningAmber.withOpacity(0.1),
              shape: BoxShape.circle,
            ),
            child: const Icon(Icons.money, color: AppTheme.warningAmber, size: 28),
          ),
          const SizedBox(width: 16),

          // Text Content
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  AppLocalizations.of(context)!.paymentCash,
                  style: const TextStyle(
                    fontWeight: FontWeight.bold,
                    fontSize: 16,
                    color: AppTheme.textPrimary,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  AppLocalizations.of(context)!.paymentCashDescription,
                  style: TextStyle(
                    fontSize: 13,
                    color: AppTheme.textMuted,
                  ),
                ),
                const SizedBox(height: 8),
                // Reference the office staff will type into "Record Cash".
                Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: AppTheme.primaryIndigo.withOpacity(0.08),
                    borderRadius: BorderRadius.circular(6),
                  ),
                  child: Row(
                    children: [
                      const Icon(Icons.tag, size: 14, color: AppTheme.primaryIndigo),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(
                          'Reference: INV-${invoice.invoiceId.toString().padLeft(6, '0')}',
                          style: const TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w700,
                            color: AppTheme.primaryIndigo,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 6),
                Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: AppTheme.infoBlue.withOpacity(0.1),
                    borderRadius: BorderRadius.circular(6),
                  ),
                  child: Row(
                    children: [
                      Icon(Icons.info, size: 14, color: AppTheme.infoBlue),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(
                          AppLocalizations.of(context)!.paymentCashOfficeHours,
                          style: TextStyle(
                            fontSize: 11,
                            color: AppTheme.infoBlue,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _payWithKNET(InvoiceModel invoice) async {
    Navigator.pop(context); // Close bottom sheet

    final authViewModel = context.read<AuthViewModel>();
    final viewModel = context.read<PaymentViewModel>();

    if (authViewModel.tenantId == null) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(AppLocalizations.of(context)!.paymentAuthError),
            backgroundColor: AppTheme.errorRed,
          ),
        );
      }
      return;
    }

    final response = await viewModel.initiateKNETPayment(
      invoice: invoice,
      tenantId: authViewModel.tenantId!,
    );

    if (!mounted) return;

    if (response != null && response.success && response.redirectUrl != null) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(AppLocalizations.of(context)!.paymentRedirectingKnet),
          backgroundColor: AppTheme.primaryIndigo,
          duration: const Duration(seconds: 2),
        ),
      );

      // Redirect to KNET gateway (works on both web and mobile)
      platform_launcher.redirectToUrl(response.redirectUrl!);
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(viewModel.error ?? response?.errorMessage ?? AppLocalizations.of(context)!.paymentInitiateFailed),
          backgroundColor: AppTheme.errorRed,
        ),
      );
    }
  }

  void _showBankTransferDialog(InvoiceModel invoice) {
    showDialog(
      context: context,
      barrierDismissible: false,
      builder: (context) => BankTransferDialog(invoice: invoice),
    );
  }

  Widget _buildEmptyState() {
    return Center(
      child: SingleChildScrollView(
        physics: const AlwaysScrollableScrollPhysics(),
        child: Padding(
          padding: const EdgeInsets.all(40),
          child: Container(
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
                Container(
                  width: 80,
                  height: 80,
                  decoration: BoxDecoration(
                    color: AppTheme.primaryIndigo.withOpacity(0.1),
                    shape: BoxShape.circle,
                  ),
                  child: const Icon(
                    Icons.receipt_long,
                    size: 40,
                    color: AppTheme.primaryIndigo,
                  ),
                ),
                const SizedBox(height: 24),
                Text(
                  AppLocalizations.of(context)!.paymentNoPendingInvoices,
                  style: const TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.bold,
                    color: AppTheme.textPrimary,
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  AppLocalizations.of(context)!.paymentNoPendingInvoicesBody,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontSize: 14,
                    color: AppTheme.textMuted,
                  ),
                ),
                const SizedBox(height: 24),
                ElevatedButton.icon(
                  onPressed: () => Navigator.of(context).pop(),
                  icon: Icon(Directionality.of(context) == TextDirection.rtl
                      ? Icons.arrow_forward
                      : Icons.arrow_back),
                  label: Text(AppLocalizations.of(context)!.backToDashboard),
                  style: ElevatedButton.styleFrom(
                    backgroundColor: AppTheme.primaryIndigo,
                    foregroundColor: Colors.white,
                    padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(8),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildErrorState(String error) {
    return Center(
      child: SingleChildScrollView(
        physics: const AlwaysScrollableScrollPhysics(),
        child: Padding(
          padding: const EdgeInsets.all(40),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.error_outline, size: 64, color: AppTheme.errorRed),
              const SizedBox(height: 16),
              Text(
                AppLocalizations.of(context)!.paymentErrorLoadingInvoices,
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.bold,
                    ),
              ),
              const SizedBox(height: 8),
              Text(
                error,
                textAlign: TextAlign.center,
                style: TextStyle(color: AppTheme.textMuted),
              ),
              const SizedBox(height: 24),
              ElevatedButton.icon(
                onPressed: _loadPendingInvoices,
                icon: const Icon(Icons.refresh),
                label: Text(AppLocalizations.of(context)!.retry),
                style: ElevatedButton.styleFrom(
                  backgroundColor: AppTheme.primaryIndigo,
                  foregroundColor: Colors.white,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Bank Transfer Dialog
class BankTransferDialog extends StatefulWidget {
  final InvoiceModel invoice;

  const BankTransferDialog({super.key, required this.invoice});

  @override
  State<BankTransferDialog> createState() => _BankTransferDialogState();
}

class _BankTransferDialogState extends State<BankTransferDialog> {
  final _formKey = GlobalKey<FormState>();
  final _bankNameController = TextEditingController();
  final _referenceController = TextEditingController();
  final _notesController = TextEditingController();
  DateTime _transferDate = DateTime.now();
  String? _selectedBank;
  bool _isSubmitting = false;
  // Mandatory transfer-proof image — the backend rejects a bank transfer
  // submitted without a proof file.
  XFile? _proof;
  final ImagePicker _picker = ImagePicker();

  final List<String> _banks = [
    'National Bank of Kuwait',
    'Gulf Bank',
    'Commercial Bank of Kuwait',
    'Burgan Bank',
    'Kuwait Finance House',
    'Al Ahli Bank of Kuwait',
    'Boubyan Bank',
    'Kuwait International Bank',
    'Other',
  ];

  @override
  void dispose() {
    _bankNameController.dispose();
    _referenceController.dispose();
    _notesController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Dialog(
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      child: Container(
        constraints: const BoxConstraints(maxWidth: 500),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Header
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(20),
              decoration: const BoxDecoration(
                gradient: AppTheme.primaryGradient,
                borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
              ),
              child: Row(
                children: [
                  const Icon(Icons.account_balance, color: Colors.white),
                  const SizedBox(width: 12),
                  Text(
                    AppLocalizations.of(context)!.paymentSubmitBankTransferTitle,
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 18,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const Spacer(),
                  IconButton(
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(Icons.close, color: Colors.white),
                  ),
                ],
              ),
            ),

            // Body
            Flexible(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(20),
                child: Form(
                  key: _formKey,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      // Instructions
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: AppTheme.infoBlue.withOpacity(0.1),
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Icon(Icons.info, size: 18, color: AppTheme.infoBlue),
                                const SizedBox(width: 8),
                                Text(
                                  AppLocalizations.of(context)!.paymentBankInstructionsTitle,
                                  style: const TextStyle(
                                    fontWeight: FontWeight.bold,
                                    color: AppTheme.infoBlue,
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 8),
                            Text(
                              AppLocalizations.of(context)!.paymentBankInstructionsBody,
                              style: TextStyle(
                                fontSize: 13,
                                color: AppTheme.infoBlue,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 20),

                      // Amount (readonly)
                      TextFormField(
                        initialValue: CurrencyUtils.formatAmount(widget.invoice.totalAmount),
                        readOnly: true,
                        decoration: InputDecoration(
                          labelText: AppLocalizations.of(context)!.paymentAmountKwd,
                          prefixIcon: const Icon(Icons.money),
                          border: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(8),
                          ),
                          filled: true,
                          fillColor: AppTheme.backgroundLight,
                        ),
                      ),
                      const SizedBox(height: 16),

                      // Transfer Date
                      InkWell(
                        onTap: _selectDate,
                        child: InputDecorator(
                          decoration: InputDecoration(
                            labelText: AppLocalizations.of(context)!.paymentTransferDate,
                            prefixIcon: const Icon(Icons.calendar_today),
                            border: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(8),
                            ),
                          ),
                          child: Text(AppDateUtils.formatShortDate(_transferDate)),
                        ),
                      ),
                      const SizedBox(height: 16),

                      // Bank Selection
                      DropdownButtonFormField<String>(
                        value: _selectedBank,
                        decoration: InputDecoration(
                          labelText: AppLocalizations.of(context)!.paymentBankName,
                          prefixIcon: const Icon(Icons.account_balance),
                          border: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(8),
                          ),
                        ),
                        items: _banks.map((bank) {
                          return DropdownMenuItem(value: bank, child: Text(bank));
                        }).toList(),
                        onChanged: (value) {
                          setState(() {
                            _selectedBank = value;
                          });
                        },
                        validator: (value) {
                          if (value == null || value.isEmpty) {
                            return AppLocalizations.of(context)!.paymentSelectBankValidation;
                          }
                          return null;
                        },
                      ),
                      const SizedBox(height: 16),

                      // Reference Number
                      TextFormField(
                        controller: _referenceController,
                        decoration: InputDecoration(
                          labelText: AppLocalizations.of(context)!.paymentReferenceOptional,
                          prefixIcon: const Icon(Icons.tag),
                          border: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(8),
                          ),
                        ),
                      ),
                      const SizedBox(height: 16),

                      // Notes
                      TextFormField(
                        controller: _notesController,
                        maxLines: 3,
                        decoration: InputDecoration(
                          labelText: AppLocalizations.of(context)!.paymentNotesOptional,
                          prefixIcon: const Padding(
                            padding: EdgeInsets.only(bottom: 48),
                            child: Icon(Icons.notes),
                          ),
                          border: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(8),
                          ),
                        ),
                      ),
                      const SizedBox(height: 16),

                      // Transfer proof — mandatory. The backend rejects a
                      // submission without an attached receipt image.
                      Text(
                        AppLocalizations.of(context)!.paymentTransferProof,
                        style: const TextStyle(
                            fontWeight: FontWeight.w600,
                            color: AppTheme.textPrimary),
                      ),
                      const SizedBox(height: 8),
                      if (_proof == null)
                        Row(
                          children: [
                            OutlinedButton.icon(
                              onPressed: () async {
                                final shot = await _picker.pickImage(
                                  source: ImageSource.camera,
                                  maxWidth: 1920,
                                  imageQuality: 80,
                                );
                                if (shot != null) {
                                  setState(() => _proof = shot);
                                }
                              },
                              icon: const Icon(Icons.photo_camera_outlined,
                                  size: 18),
                              label: Text(AppLocalizations.of(context)!.maintenanceCamera),
                            ),
                            const SizedBox(width: 8),
                            OutlinedButton.icon(
                              onPressed: () async {
                                final pick = await _picker.pickImage(
                                  source: ImageSource.gallery,
                                  maxWidth: 1920,
                                  imageQuality: 80,
                                );
                                if (pick != null) {
                                  setState(() => _proof = pick);
                                }
                              },
                              icon: const Icon(Icons.photo_library_outlined,
                                  size: 18),
                              label: Text(AppLocalizations.of(context)!.maintenanceGallery),
                            ),
                          ],
                        )
                      else
                        Row(
                          children: [
                            ClipRRect(
                              borderRadius: BorderRadius.circular(8),
                              child: Image.file(
                                File(_proof!.path),
                                width: 64,
                                height: 64,
                                fit: BoxFit.cover,
                              ),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Text(
                                _proof!.name,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                    fontSize: 12,
                                    color: AppTheme.textMuted),
                              ),
                            ),
                            IconButton(
                              icon: const Icon(Icons.close),
                              onPressed: () =>
                                  setState(() => _proof = null),
                            ),
                          ],
                        ),
                    ],
                  ),
                ),
              ),
            ),

            // Footer
            Container(
              padding: const EdgeInsets.all(20),
              decoration: BoxDecoration(
                color: AppTheme.backgroundLight,
                borderRadius: const BorderRadius.vertical(bottom: Radius.circular(16)),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: () => Navigator.pop(context),
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(8),
                        ),
                      ),
                      child: Text(AppLocalizations.of(context)!.cancel),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: ElevatedButton(
                      onPressed: _isSubmitting ? null : _submitTransfer,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: AppTheme.successGreen,
                        foregroundColor: Colors.white,
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(8),
                        ),
                      ),
                      child: _isSubmitting
                          ? const SizedBox(
                              width: 20,
                              height: 20,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                              ),
                            )
                          : Text(AppLocalizations.of(context)!.submitTransfer),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _selectDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _transferDate,
      firstDate: DateTime.now().subtract(const Duration(days: 30)),
      lastDate: DateTime.now(),
    );
    if (picked != null) {
      setState(() {
        _transferDate = picked;
      });
    }
  }

  Future<void> _submitTransfer() async {
    if (!_formKey.currentState!.validate()) return;

    if (_proof == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(AppLocalizations.of(context)!.paymentAttachProofFirst),
          backgroundColor: AppTheme.errorRed,
        ),
      );
      return;
    }

    setState(() {
      _isSubmitting = true;
    });

    try {
      final viewModel = context.read<PaymentViewModel>();
      final success = await viewModel.submitBankTransfer(
        invoiceId: widget.invoice.invoiceId,
        amount: widget.invoice.totalAmount,
        bankName: _selectedBank!,
        transferDate: _transferDate,
        proofFilePath: _proof!.path,
        referenceNumber: _referenceController.text.trim(),
        notes: _notesController.text.trim(),
      );

      if (mounted) {
        Navigator.pop(context);

        if (success) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text(AppLocalizations.of(context)!.paymentBankTransferSuccess),
              backgroundColor: AppTheme.successGreen,
            ),
          );
          // Refresh invoices
          viewModel.loadPendingInvoices();
        } else {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text(viewModel.error ?? AppLocalizations.of(context)!.paymentSubmitTransferFailed),
              backgroundColor: AppTheme.errorRed,
            ),
          );
        }
      }
    } finally {
      if (mounted) {
        setState(() {
          _isSubmitting = false;
        });
      }
    }
  }
}
