/// Login request model
class LoginRequest {
  final String civilId;
  final String password;
  final bool rememberMe;

  LoginRequest({
    required this.civilId,
    required this.password,
    this.rememberMe = false,
  });

  Map<String, dynamic> toJson() {
    // Backend matches `civilId` against Tenant.GovernmentId. PACI on the
    // tenant record is a property identifier — never the login key.
    return {
      'civilId': civilId,
      'password': password,
    };
  }
}

/// Login response model
class LoginResponse {
  final bool success;
  final String message;
  final int? tenantId;
  final String? token;
  final String? firstName;
  final String? lastName;
  final String? email;
  final String? phone;
  final String? paciNumber;
  final String? company;

  LoginResponse({
    required this.success,
    required this.message,
    this.tenantId,
    this.token,
    this.firstName,
    this.lastName,
    this.email,
    this.phone,
    this.paciNumber,
    this.company,
  });

  /// Full name of the logged in tenant
  String get fullName => '${firstName ?? ''} ${lastName ?? ''}'.trim();

  factory LoginResponse.fromJson(Map<String, dynamic> json) {
    final data = json['data'];
    return LoginResponse(
      success: json['success'] ?? false,
      message: json['message'] ?? '',
      tenantId: data?['tenantId'],
      token: data?['token'],
      firstName: data?['firstName'],
      lastName: data?['lastName'],
      email: data?['email'],
      phone: data?['phone'],
      paciNumber: data?['paciNumber'],
      company: data?['company'],
    );
  }

  factory LoginResponse.error(String message) {
    return LoginResponse(
      success: false,
      message: message,
    );
  }
}

/// Change password request model
class ChangePasswordRequest {
  final int tenantId;
  final String oldPassword;
  final String newPassword;

  ChangePasswordRequest({
    required this.tenantId,
    required this.oldPassword,
    required this.newPassword,
  });

  Map<String, dynamic> toJson() {
    return {
      'tenantId': tenantId,
      'oldPassword': oldPassword,
      'newPassword': newPassword,
    };
  }
}
