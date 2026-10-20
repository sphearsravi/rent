import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/currency_utils.dart';
import '../../l10n/app_localizations.dart';

/// What the tenant decided to bring to the counter.
class CashRequestChoice {
  const CashRequestChoice({required this.amount, this.notes});

  /// null = the full outstanding balance (server computes it).
  final double? amount;
  final String? notes;
}

/// Asks the tenant how much cash they will pay at the office before the
/// request is sent. Defaults to the full balance; a smaller amount is a
/// partial payment and the dialog shows what will remain.
///
/// Returns null when dismissed.
Future<CashRequestChoice?> showCashRequestDialog(
  BuildContext context, {
  required double balance,
}) {
  return showDialog<CashRequestChoice>(
    context: context,
    builder: (_) => _CashRequestDialog(balance: balance),
  );
}

class _CashRequestDialog extends StatefulWidget {
  const _CashRequestDialog({required this.balance});

  final double balance;

  @override
  State<_CashRequestDialog> createState() => _CashRequestDialogState();
}

class _CashRequestDialogState extends State<_CashRequestDialog> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _amountController;
  final _notesController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _amountController =
        TextEditingController(text: widget.balance.toStringAsFixed(3));
    _amountController.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _amountController.dispose();
    _notesController.dispose();
    super.dispose();
  }

  /// Accept Arabic-Indic digits and either decimal separator.
  double? _parsedAmount() {
    const arabic = '٠١٢٣٤٥٦٧٨٩';
    final buf = StringBuffer();
    // Arabic-Indic digits are single UTF-16 code units, so split('') is safe.
    for (final ch in _amountController.text.trim().split('')) {
      final i = arabic.indexOf(ch);
      buf.write(i >= 0 ? i.toString() : (ch == '٫' || ch == ',' ? '.' : ch));
    }
    return double.tryParse(buf.toString());
  }

  bool get _isFullBalance {
    final v = _parsedAmount();
    return v != null && (v - widget.balance).abs() < 0.0005;
  }

  String? _validate(String? _) {
    final l = AppLocalizations.of(context)!;
    final v = _parsedAmount();
    if (v == null || v <= 0 || v > widget.balance + 0.0005) {
      return l.paymentCashRequestAmountInvalid(
        CurrencyUtils.formatAmount(widget.balance, showCurrency: false),
      );
    }
    return null;
  }

  void _submit() {
    if (!_formKey.currentState!.validate()) return;
    final v = _parsedAmount()!;
    final notes = _notesController.text.trim();
    Navigator.of(context).pop(CashRequestChoice(
      // Full balance → let the server compute it (avoids rounding drift).
      amount: _isFullBalance ? null : double.parse(v.toStringAsFixed(3)),
      notes: notes.isEmpty ? null : notes,
    ),);
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context)!;
    final parsed = _parsedAmount();
    final remaining = (parsed != null && parsed > 0 && parsed < widget.balance)
        ? widget.balance - parsed
        : null;

    return AlertDialog(
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      title: Row(
        children: [
          const Icon(Icons.payments_outlined, color: AppTheme.warningAmber),
          const SizedBox(width: 8),
          Expanded(child: Text(l.paymentCashRequestTitle)),
        ],
      ),
      content: Form(
        key: _formKey,
        autovalidateMode: AutovalidateMode.onUserInteraction,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                l.paymentCashRequestIntro,
                style: const TextStyle(
                  fontSize: 13,
                  color: AppTheme.textSecondary,
                ),
              ),
              const SizedBox(height: 16),
              TextFormField(
                controller: _amountController,
                keyboardType:
                    const TextInputType.numberWithOptions(decimal: true),
                inputFormatters: [
                  FilteringTextInputFormatter.allow(RegExp(r'[0-9٠-٩.,٫]')),
                ],
                validator: _validate,
                decoration: InputDecoration(
                  labelText: l.paymentCashRequestAmountLabel,
                  prefixIcon: const Icon(Icons.money),
                  helperText:
                      '${l.paymentAmountDueLabel}: ${CurrencyUtils.formatAmount(widget.balance)}',
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                  suffixIcon: _isFullBalance
                      ? null
                      : TextButton(
                          onPressed: () => _amountController.text =
                              widget.balance.toStringAsFixed(3),
                          child: Text(l.paymentCashRequestFullBalance),
                        ),
                ),
              ),
              if (remaining != null) ...[
                const SizedBox(height: 10),
                Container(
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                    // Flutter 3.24 — withValues() only exists from 3.27.
                    // ignore: deprecated_member_use
                    color: AppTheme.warningAmber.withOpacity(0.1),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Row(
                    children: [
                      const Icon(
                        Icons.info_outline,
                        size: 16,
                        color: AppTheme.warningAmber,
                      ),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(
                          l.paymentCashRequestPartialHint(
                            CurrencyUtils.formatAmount(remaining, showCurrency: false),
                          ),
                          style: const TextStyle(fontSize: 12),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
              const SizedBox(height: 16),
              TextFormField(
                controller: _notesController,
                maxLines: 2,
                maxLength: 200,
                decoration: InputDecoration(
                  labelText: l.paymentNotesOptional,
                  prefixIcon: const Icon(Icons.notes),
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l.cancel),
        ),
        ElevatedButton.icon(
          onPressed: _submit,
          icon: const Icon(Icons.notifications_active_outlined, size: 18),
          label: Text(l.paymentCashRequestConfirm),
          style: ElevatedButton.styleFrom(
            backgroundColor: AppTheme.warningAmber,
            foregroundColor: Colors.white,
          ),
        ),
      ],
    );
  }
}
