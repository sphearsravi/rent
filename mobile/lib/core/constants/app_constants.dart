/// Application-wide constants
class AppConstants {
  // API base URL. Defaults to the live production backend (alrtam.co, HTTPS) so
  // both Android and iOS release builds hit the same server. Override only for
  // local dev or a different environment:
  //   flutter build apk --dart-define=API_BASE_URL=https://www.alrtam.co/api
  //   flutter build web --dart-define=API_BASE_URL=https://www.alrtam.co/api
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    // Default is the live HTTPS backend so release builds can't silently ship a
    // localhost URL. For local dev, override:
    //   flutter run --dart-define=API_BASE_URL=http://localhost:5032/api
    defaultValue: 'https://www.alrtam.co/api',
  );
  /// True when the app is running against a localhost / private-network backend.
  /// Useful for guarding behaviour that must never trigger in production.
  static bool get isLocalDev =>
      baseUrl.contains('localhost') ||
      baseUrl.contains('127.0.0.1') ||
      baseUrl.startsWith('http://10.') ||
      baseUrl.startsWith('http://192.168.');

  // API Endpoints
  static const String loginEndpoint = '/tenant/login';
  static const String profileEndpoint = '/tenant/{tenantId}/profile';
  static const String dashboardEndpoint = '/tenant/{tenantId}/dashboard';
  static const String leasesEndpoint = '/tenant/{tenantId}/leases';
  static const String paymentsEndpoint = '/tenant/{tenantId}/payments';
  static const String maintenanceEndpoint = '/tenant/{tenantId}/maintenance-requests';
  static const String notificationsEndpoint = '/tenant/{tenantId}/notifications';
  static const String changePasswordEndpoint = '/tenant/change-password';

  // Storage Keys
  static const String tokenKey = 'auth_token';
  static const String tenantIdKey = 'tenant_id';
  static const String tenantNameKey = 'tenant_name';
  static const String tenantEmailKey = 'tenant_email';
  static const String rememberMeKey = 'remember_me';
  // Remembered login credentials (stored in ENCRYPTED secure storage, only when
  // "Remember me" is checked) so the login form can prefill them next time.
  static const String savedCivilIdKey = 'saved_civil_id';
  static const String savedPasswordKey = 'saved_password';

  // App Info
  static const String appName = 'Alrtam';
  static const String appVersion = '1.0.0';
  static const String currency = 'KWD';

  // Pagination
  static const int defaultPageSize = 20;

  // Timeouts
  static const int connectionTimeout = 30000; // 30 seconds
  static const int receiveTimeout = 30000; // 30 seconds

  // Maintenance Categories
  static const List<String> maintenanceCategories = [
    'Plumbing',
    'Electrical',
    'HVAC',
    'Appliance',
    'Structural',
    'Other',
  ];

  // Maintenance Priorities
  static const List<String> maintenancePriorities = [
    'Low',
    'Medium',
    'High',
    'Emergency',
  ];

  // Payment Methods
  static const List<String> paymentMethods = [
    'Cash',
    'KNET',
    'BankTransfer',
    'Cheque',
  ];
}
