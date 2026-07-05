/// Tenant model representing a tenant in the system
class TenantModel {
  final int tenantId;
  final String? firstName;
  final String? lastName;
  final String? email;
  final String? phone;
  final String? paciNumber;
  final String? governmentId;
  final DateTime? dateOfBirth;
  final String? company;
  final String? status;
  final String? emergencyContact;
  final String? emergencyPhone;

  TenantModel({
    required this.tenantId,
    this.firstName,
    this.lastName,
    this.email,
    this.phone,
    this.paciNumber,
    this.governmentId,
    this.dateOfBirth,
    this.company,
    this.status,
    this.emergencyContact,
    this.emergencyPhone,
  });

  /// Full name of the tenant
  String get fullName => '${firstName ?? ''} ${lastName ?? ''}'.trim();

  /// Initials of the tenant's name
  String get initials {
    final first = firstName?.isNotEmpty == true ? firstName![0].toUpperCase() : '';
    final last = lastName?.isNotEmpty == true ? lastName![0].toUpperCase() : '';
    return '$first$last'.isNotEmpty ? '$first$last' : 'TN';
  }

  /// Create TenantModel from JSON
  factory TenantModel.fromJson(Map<String, dynamic> json) {
    return TenantModel(
      tenantId: json['tenantId'] ?? 0,
      firstName: json['firstName'],
      lastName: json['lastName'],
      email: json['email'],
      phone: json['phone'],
      paciNumber: json['paciNumber'],
      governmentId: json['governmentId'],
      dateOfBirth: json['dateOfBirth'] != null
          ? DateTime.tryParse(json['dateOfBirth'])
          : null,
      company: json['company'],
      status: json['status'],
      emergencyContact: json['emergencyContact'],
      emergencyPhone: json['emergencyPhone'],
    );
  }

  /// Convert TenantModel to JSON
  Map<String, dynamic> toJson() {
    return {
      'tenantId': tenantId,
      'firstName': firstName,
      'lastName': lastName,
      'email': email,
      'phone': phone,
      'paciNumber': paciNumber,
      'governmentId': governmentId,
      'dateOfBirth': dateOfBirth?.toIso8601String(),
      'company': company,
      'status': status,
      'emergencyContact': emergencyContact,
      'emergencyPhone': emergencyPhone,
    };
  }

  /// Create a copy with modified fields
  TenantModel copyWith({
    int? tenantId,
    String? firstName,
    String? lastName,
    String? email,
    String? phone,
    String? paciNumber,
    String? governmentId,
    DateTime? dateOfBirth,
    String? company,
    String? status,
    String? emergencyContact,
    String? emergencyPhone,
  }) {
    return TenantModel(
      tenantId: tenantId ?? this.tenantId,
      firstName: firstName ?? this.firstName,
      lastName: lastName ?? this.lastName,
      email: email ?? this.email,
      phone: phone ?? this.phone,
      paciNumber: paciNumber ?? this.paciNumber,
      governmentId: governmentId ?? this.governmentId,
      dateOfBirth: dateOfBirth ?? this.dateOfBirth,
      company: company ?? this.company,
      status: status ?? this.status,
      emergencyContact: emergencyContact ?? this.emergencyContact,
      emergencyPhone: emergencyPhone ?? this.emergencyPhone,
    );
  }
}
