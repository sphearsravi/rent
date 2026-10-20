/// Invoice model representing a pending invoice
class InvoiceModel {
  final int invoiceId;
  final int? leaseId;
  final int? tenantId;
  final int? propertyId;
  final String? propertyName;
  final String? unitNumber;
  final double totalAmount;
  final double? paidAmount;
  final double? balanceAmount;
  final DateTime dueDate;
  final DateTime? invoiceDate;
  final String status;
  final String? description;

  InvoiceModel({
    required this.invoiceId,
    this.leaseId,
    this.tenantId,
    this.propertyId,
    this.propertyName,
    this.unitNumber,
    required this.totalAmount,
    this.paidAmount,
    this.balanceAmount,
    required this.dueDate,
    this.invoiceDate,
    required this.status,
    this.description,
  });

  /// Check if invoice is overdue
  bool get isOverdue => status.toLowerCase() == 'overdue';

  /// Check if invoice is pending
  bool get isPending => status.toLowerCase() == 'pending';

  /// Check if invoice is paid
  bool get isPaid => status.toLowerCase() == 'paid';

  /// Get remaining balance
  double get balance => balanceAmount ?? (totalAmount - (paidAmount ?? 0));

  factory InvoiceModel.fromJson(Map<String, dynamic> json) {
    return InvoiceModel(
      invoiceId: (json['invoiceId'] is int) ? json['invoiceId'] : int.tryParse(json['invoiceId']?.toString() ?? '0') ?? 0,
      leaseId: json['leaseId'] != null ? (json['leaseId'] is int ? json['leaseId'] : int.tryParse(json['leaseId'].toString())) : null,
      tenantId: json['tenantId'] != null ? (json['tenantId'] is int ? json['tenantId'] : int.tryParse(json['tenantId'].toString())) : null,
      propertyId: json['propertyId'] != null ? (json['propertyId'] is int ? json['propertyId'] : int.tryParse(json['propertyId'].toString())) : null,
      propertyName: json['propertyName'],
      unitNumber: json['unitNumber'],
      totalAmount: (json['totalAmount'] ?? 0).toDouble(),
      paidAmount: json['paidAmount']?.toDouble(),
      balanceAmount: json['balanceAmount']?.toDouble(),
      dueDate: DateTime.parse(json['dueDate'] ?? DateTime.now().toIso8601String()),
      invoiceDate: json['invoiceDate'] != null ? DateTime.parse(json['invoiceDate']) : null,
      status: json['status'] ?? 'Pending',
      description: json['description'],
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'invoiceId': invoiceId,
      'leaseId': leaseId,
      'tenantId': tenantId,
      'propertyId': propertyId,
      'propertyName': propertyName,
      'unitNumber': unitNumber,
      'totalAmount': totalAmount,
      'paidAmount': paidAmount,
      'balanceAmount': balanceAmount,
      'dueDate': dueDate.toIso8601String(),
      'invoiceDate': invoiceDate?.toIso8601String(),
      'status': status,
      'description': description,
    };
  }
}

/// Payment model representing a rent payment
class PaymentModel {
  final int paymentId;
  final int? leaseId;
  final String? propertyName;
  final String? unitNumber;
  final double amountPaid;
  final DateTime paymentDate;
  final String paymentMethod;
  final String paymentStatus;
  final String? transactionReference;
  final String? notes;
  final String? receiptNumber;

  PaymentModel({
    required this.paymentId,
    this.leaseId,
    this.propertyName,
    this.unitNumber,
    required this.amountPaid,
    required this.paymentDate,
    required this.paymentMethod,
    required this.paymentStatus,
    this.transactionReference,
    this.notes,
    this.receiptNumber,
  });

  /// Normalised status key: lowercase with spaces/underscores removed, so the
  /// backend's `PendingVerification` enum, "Pending Verification" and
  /// "pending_verification" all compare equal.
  String get _statusKey => paymentStatus
      .toLowerCase()
      .replaceAll(' ', '')
      .replaceAll('_', '');

  /// Check if payment is completed/approved
  bool get isCompleted => _statusKey == 'completed' || _statusKey == 'approved';

  /// Check if payment is pending (incl. awaiting office verification — the
  /// state every bank-transfer and cash payment sits in until approved).
  bool get isPending => _statusKey == 'pending' || _statusKey == 'pendingverification';

  /// Check if payment is rejected
  bool get isRejected =>
      _statusKey == 'rejected' || _statusKey == 'failed' || _statusKey == 'cancelled';

  /// Get status color name for UI
  String get statusColorName {
    switch (_statusKey) {
      case 'completed':
      case 'approved':
        return 'green';
      case 'pending':
      case 'pendingverification':
        return 'orange';
      case 'rejected':
      case 'failed':
      case 'cancelled':
        return 'red';
      default:
        return 'grey';
    }
  }

  /// Create PaymentModel from JSON
  factory PaymentModel.fromJson(Map<String, dynamic> json) {
    return PaymentModel(
      paymentId: json['paymentId'] ?? 0,
      leaseId: json['leaseId'],
      propertyName: json['propertyName'],
      unitNumber: json['unitNumber'],
      amountPaid: (num.tryParse('${json['amountPaid'] ?? 0}') ?? 0).toDouble(),
      // tryParse: a missing/malformed date must not crash the whole list.
      paymentDate: DateTime.tryParse('${json['paymentDate'] ?? ''}') ?? DateTime.now(),
      paymentMethod: json['paymentMethod'] ?? 'Unknown',
      paymentStatus: json['paymentStatus'] ?? 'Pending',
      transactionReference: json['transactionReference'],
      notes: json['notes'],
      receiptNumber: json['receiptNumber'],
    );
  }

  /// Convert PaymentModel to JSON
  Map<String, dynamic> toJson() {
    return {
      'paymentId': paymentId,
      'leaseId': leaseId,
      'propertyName': propertyName,
      'unitNumber': unitNumber,
      'amountPaid': amountPaid,
      'paymentDate': paymentDate.toIso8601String(),
      'paymentMethod': paymentMethod,
      'paymentStatus': paymentStatus,
      'transactionReference': transactionReference,
      'notes': notes,
      'receiptNumber': receiptNumber,
    };
  }
}

/// Request model for submitting a payment
class SubmitPaymentRequest {
  final int leaseId;
  final double amount;
  final String paymentMethod;
  final String? transactionReference;
  final String? notes;

  SubmitPaymentRequest({
    required this.leaseId,
    required this.amount,
    required this.paymentMethod,
    this.transactionReference,
    this.notes,
  });

  Map<String, dynamic> toJson() {
    return {
      'leaseId': leaseId,
      'amount': amount,
      'paymentMethod': paymentMethod,
      'transactionReference': transactionReference,
      'notes': notes,
    };
  }
}

/// Request model for initiating a KNET payment
class KnetPaymentRequest {
  final int invoiceId;
  final int tenantId;
  final int propertyId;
  final int leaseId;
  final double amount;
  final String language;

  KnetPaymentRequest({
    required this.invoiceId,
    required this.tenantId,
    required this.propertyId,
    required this.leaseId,
    required this.amount,
    this.language = 'en',
  });

  Map<String, dynamic> toJson() {
    return {
      'invoiceId': invoiceId,
      'tenantId': tenantId,
      'propertyId': propertyId,
      'leaseId': leaseId,
      'amount': amount,
      'language': language,
    };
  }
}

/// Response model from KNET payment initiation
class KnetPaymentResponse {
  final bool success;
  final String? paymentId;
  final String? redirectUrl;
  final String? status;
  final String? transactionReference;
  final String? errorMessage;

  KnetPaymentResponse({
    required this.success,
    this.paymentId,
    this.redirectUrl,
    this.status,
    this.transactionReference,
    this.errorMessage,
  });

  factory KnetPaymentResponse.fromJson(Map<String, dynamic> json) {
    return KnetPaymentResponse(
      success: json['success'] ?? false,
      paymentId: json['paymentId']?.toString(),
      redirectUrl: json['redirectUrl'],
      status: json['status'],
      transactionReference: json['transactionReference'],
      errorMessage: json['errorMessage'],
    );
  }
}
