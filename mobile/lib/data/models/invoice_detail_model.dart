/// Invoice detail returned by GET /api/tenant/{tenantId}/invoices/{invoiceId}.
///
/// Distinct from [InvoiceModel] (in payment_model.dart) because the detail
/// response carries the full property address, internal notes, and the list
/// of [InvoicePaymentSummary] records that have been applied to the invoice.
class InvoiceDetailModel {
  final int invoiceId;
  final int? leaseId;
  final int? tenantId;
  final int? propertyId;
  final String? propertyName;
  final String? propertyAddress;
  final String? unitNumber;
  final String? invoiceType;
  final double totalAmount;
  final double paidAmount;
  final double balanceAmount;
  final DateTime invoiceDate;
  final DateTime dueDate;
  final String status;
  final String? notes;
  final List<InvoicePaymentSummary> payments;

  InvoiceDetailModel({
    required this.invoiceId,
    required this.totalAmount,
    required this.paidAmount,
    required this.balanceAmount,
    required this.invoiceDate,
    required this.dueDate,
    required this.status,
    this.leaseId,
    this.tenantId,
    this.propertyId,
    this.propertyName,
    this.propertyAddress,
    this.unitNumber,
    this.invoiceType,
    this.notes,
    this.payments = const [],
  });

  bool get isPaid => status.toLowerCase() == 'paid';
  bool get isPartiallyPaid => status.toLowerCase() == 'partially paid';
  bool get isOverdue => status.toLowerCase() == 'overdue';
  bool get isPending => status.toLowerCase() == 'pending';
  bool get isOutstanding => isPending || isOverdue || isPartiallyPaid;

  factory InvoiceDetailModel.fromJson(Map<String, dynamic> json) {
    final paymentsRaw = (json['payments'] as List<dynamic>?) ?? const [];

    return InvoiceDetailModel(
      invoiceId: _asInt(json['invoiceId']) ?? 0,
      leaseId: _asInt(json['leaseId']),
      tenantId: _asInt(json['tenantId']),
      propertyId: _asInt(json['propertyId']),
      propertyName: json['propertyName'] as String?,
      propertyAddress: json['propertyAddress'] as String?,
      unitNumber: json['unitNumber'] as String?,
      invoiceType: json['invoiceType'] as String?,
      totalAmount: _asDouble(json['totalAmount']) ?? 0.0,
      paidAmount: _asDouble(json['paidAmount']) ?? 0.0,
      balanceAmount: _asDouble(json['balanceAmount']) ?? 0.0,
      invoiceDate: DateTime.parse(
        json['invoiceDate'] ?? DateTime.now().toIso8601String(),
      ),
      dueDate: DateTime.parse(
        json['dueDate'] ?? DateTime.now().toIso8601String(),
      ),
      status: json['status'] as String? ?? 'Pending',
      notes: json['notes'] as String?,
      payments: paymentsRaw
          .whereType<Map<String, dynamic>>()
          .map(InvoicePaymentSummary.fromJson)
          .toList(),
    );
  }
}

/// A short summary of a payment that has been applied to an invoice — used
/// in the invoice detail screen's "Payments" section. Not the full
/// [PaymentModel]; just what's needed for a list row.
class InvoicePaymentSummary {
  final int paymentId;
  final DateTime paymentDate;
  final double amountPaid;
  final String paymentType;
  final String status;
  final String? receiptNumber;
  final String? transactionReference;

  InvoicePaymentSummary({
    required this.paymentId,
    required this.paymentDate,
    required this.amountPaid,
    required this.paymentType,
    required this.status,
    this.receiptNumber,
    this.transactionReference,
  });

  bool get isCompleted => status.toLowerCase() == 'completed';

  factory InvoicePaymentSummary.fromJson(Map<String, dynamic> json) {
    return InvoicePaymentSummary(
      paymentId: _asInt(json['paymentId']) ?? 0,
      paymentDate: DateTime.parse(
        json['paymentDate'] ?? DateTime.now().toIso8601String(),
      ),
      amountPaid: _asDouble(json['amountPaid']) ?? 0.0,
      paymentType: json['paymentType'] as String? ?? '',
      status: json['status'] as String? ?? '',
      receiptNumber: json['receiptNumber'] as String?,
      transactionReference: json['transactionReference'] as String?,
    );
  }
}

/// Defensive int parse: backend sometimes returns numbers as strings.
int? _asInt(Object? value) {
  if (value == null) return null;
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value.toString());
}

/// Defensive double parse: same reason.
double? _asDouble(Object? value) {
  if (value == null) return null;
  if (value is double) return value;
  if (value is num) return value.toDouble();
  return double.tryParse(value.toString());
}
