/// Lease model representing a rental lease agreement
class LeaseModel {
  final int leaseId;
  final String? propertyName;
  final String? propertyAddress;
  final String? area;
  final String? unitNumber;
  final double monthlyRent;
  final double securityDeposit;
  final DateTime leaseStartDate;
  final DateTime leaseEndDate;
  final int paymentDueDay;
  final String? leaseType;
  final String status;
  final double? lateFeeAmount;
  final int? lateFeeGracePeriod;

  LeaseModel({
    required this.leaseId,
    this.propertyName,
    this.propertyAddress,
    this.area,
    this.unitNumber,
    required this.monthlyRent,
    required this.securityDeposit,
    required this.leaseStartDate,
    required this.leaseEndDate,
    this.paymentDueDay = 1,
    this.leaseType,
    this.status = 'Active',
    this.lateFeeAmount,
    this.lateFeeGracePeriod,
  });

  /// Days remaining until lease ends
  int get daysRemaining => leaseEndDate.difference(DateTime.now()).inDays;

  /// Check if lease is expiring within 30 days
  bool get isExpiring => daysRemaining > 0 && daysRemaining <= 30;

  /// Check if lease is expired
  bool get isExpired => daysRemaining < 0;

  /// Check if lease is active
  bool get isActive => status.toLowerCase() == 'active' && !isExpired;

  /// Full property address
  String get fullAddress {
    final parts = <String>[];
    if (propertyAddress != null) parts.add(propertyAddress!);
    if (area != null) parts.add(area!);
    return parts.join(', ');
  }

  /// Property display name with unit
  String get displayName {
    if (unitNumber != null && unitNumber!.isNotEmpty) {
      return '$propertyName - Unit $unitNumber';
    }
    return propertyName ?? 'Unknown Property';
  }

  /// Create LeaseModel from JSON
  factory LeaseModel.fromJson(Map<String, dynamic> json) {
    return LeaseModel(
      leaseId: json['leaseId'] ?? 0,
      propertyName: json['propertyName'],
      propertyAddress: json['propertyAddress'],
      area: json['area'],
      unitNumber: json['unitNumber'],
      monthlyRent: (json['monthlyRent'] ?? 0).toDouble(),
      securityDeposit: (json['securityDeposit'] ?? 0).toDouble(),
      leaseStartDate: DateTime.parse(json['leaseStartDate'] ?? DateTime.now().toIso8601String()),
      leaseEndDate: DateTime.parse(json['leaseEndDate'] ?? DateTime.now().toIso8601String()),
      paymentDueDay: json['paymentDueDay'] ?? 1,
      leaseType: json['leaseType'],
      status: json['status'] ?? 'Active',
      lateFeeAmount: json['lateFeeAmount']?.toDouble(),
      lateFeeGracePeriod: json['lateFeeGracePeriod'],
    );
  }

  /// Convert LeaseModel to JSON
  Map<String, dynamic> toJson() {
    return {
      'leaseId': leaseId,
      'propertyName': propertyName,
      'propertyAddress': propertyAddress,
      'area': area,
      'unitNumber': unitNumber,
      'monthlyRent': monthlyRent,
      'securityDeposit': securityDeposit,
      'leaseStartDate': leaseStartDate.toIso8601String(),
      'leaseEndDate': leaseEndDate.toIso8601String(),
      'paymentDueDay': paymentDueDay,
      'leaseType': leaseType,
      'status': status,
      'lateFeeAmount': lateFeeAmount,
      'lateFeeGracePeriod': lateFeeGracePeriod,
    };
  }
}
