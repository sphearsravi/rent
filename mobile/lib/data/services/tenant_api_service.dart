import 'package:dio/dio.dart';

import '../models/auth_model.dart';
import '../models/tenant_model.dart';
import '../models/lease_model.dart';
import '../models/payment_model.dart';
import '../models/maintenance_model.dart';
import '../models/dashboard_model.dart';
import '../models/invoice_detail_model.dart';
import '../models/invoice_list_result.dart';
import 'api_service.dart';
import 'i_tenant_api.dart';

/// Concrete implementation of [ITenantApi] backed by Dio.
/// View-models should depend on `ITenantApi`, not on this class — see
/// `i_tenant_api.dart`. Provider/DI registrations can stay on the concrete
/// type for now; migrate gradually.
/// Server-reported version policy for the force-update gate.
class AppVersionInfo {
  final String minSupportedVersion;
  final String latestVersion;
  final String storeUrl;

  AppVersionInfo({
    required this.minSupportedVersion,
    required this.latestVersion,
    required this.storeUrl,
  });
}

class TenantApiService implements ITenantApi {
  final ApiService _apiService;

  TenantApiService(this._apiService);

  // ============ App version gate ============

  /// Fetches the server's min-supported / latest version for [platform].
  /// Anonymous endpoint, called on splash before auth. Returns null on any
  /// failure so the caller can fail-open (a network blip must not lock the
  /// user out of the app).
  Future<AppVersionInfo?> getAppVersionInfo({String platform = 'android'}) async {
    try {
      final response = await _apiService.get(
        '/app/version',
        queryParameters: {'platform': platform},
      );
      final data = response.data?['data'];
      if (data == null) return null;
      return AppVersionInfo(
        minSupportedVersion: data['minSupportedVersion'] as String? ?? '0.0.0',
        latestVersion: data['latestVersion'] as String? ?? '0.0.0',
        storeUrl: data['storeUrl'] as String? ?? '',
      );
    } catch (_) {
      return null;
    }
  }

  // ============ Authentication ============

  /// Silent token renewal. Sends the current JWT (attached by the Dio
  /// auth interceptor) and persists the freshly-issued one. Returns true
  /// on success, false on any error (the caller should treat false as
  /// "log in again").
  Future<bool> refreshToken() async {
    try {
      final response = await _apiService.post('/tenant/refresh');
      final newToken = response.data?['data']?['token'] as String?;
      if (newToken == null || newToken.isEmpty) return false;
      // Reach into the shared storage via the dio's storage service.
      // The api_service interceptor reads the token on the next request.
      await _apiService.storage.saveToken(newToken);
      return true;
    } on ApiException catch (e) {
      // Only a definite auth rejection (401/403) means "log in again". Network
      // blips, rate limiting (the refresh endpoint is throttled per IP — easy to
      // hit behind carrier NAT), and server errors must NOT log the user out:
      // the stored token is very likely still valid, so rethrow and let the
      // caller keep the session.
      if (e.kind == ApiErrorKind.unauthenticated || e.kind == ApiErrorKind.forbidden) {
        return false;
      }
      rethrow;
    }
  }

  /// Login with Civil ID and password
  @override
  Future<LoginResponse> login(LoginRequest request) async {
    try {
      final response = await _apiService.post(
        '/tenant/login',
        data: request.toJson(),
      );
      return LoginResponse.fromJson(response.data);
    } on ApiException catch (e) {
      return LoginResponse.error(e.message);
    } catch (e) {
      return LoginResponse.error('Login failed. Please try again.');
    }
  }

  /// Change password. The server rotates the security stamp and issues a NEW
  /// token — persist it, otherwise the very next request 401s and the user is
  /// logged out right after changing their password.
  @override
  Future<bool> changePassword(ChangePasswordRequest request) async {
    final response = await _apiService.post(
      '/tenant/change-password',
      data: request.toJson(),
    );
    final ok = response.data['success'] == true;
    if (ok) {
      final newToken = response.data['data']?['token'] as String?;
      if (newToken != null && newToken.isNotEmpty) {
        await _apiService.storage.saveToken(newToken);
      }
    }
    return ok;
  }

  // ============ Profile ============

  /// Get tenant profile
  @override
  Future<TenantModel> getProfile(int tenantId) async {
    final response = await _apiService.get('/tenant/$tenantId/profile');
    return TenantModel.fromJson(response.data['data']);
  }

  /// Update tenant profile
  @override
  Future<bool> updateProfile(int tenantId, Map<String, dynamic> data) async {
    final response = await _apiService.put(
      '/tenant/$tenantId/profile',
      data: data,
    );
    return response.data['success'] ?? false;
  }

  // ============ Dashboard ============

  /// Get dashboard data
  @override
  Future<DashboardModel> getDashboard(int tenantId) async {
    final response = await _apiService.get('/tenant/$tenantId/dashboard');
    return DashboardModel.fromJson(response.data['data']);
  }

  /// Get notifications
  @override
  Future<List<NotificationModel>> getNotifications(int tenantId) async {
    final response = await _apiService.get('/tenant/$tenantId/notifications');
    final List<dynamic> data = response.data['data'] ?? [];
    return data.map((e) => NotificationModel.fromJson(e)).toList();
  }

  // ============ Leases ============

  /// Get all active leases
  @override
  Future<List<LeaseModel>> getLeases(int tenantId) async {
    final response = await _apiService.get('/tenant/$tenantId/leases');
    final List<dynamic> data = response.data['data'] ?? [];
    return data.map((e) => LeaseModel.fromJson(e)).toList();
  }

  /// Get lease details
  @override
  Future<LeaseModel> getLeaseDetails(int tenantId, int leaseId) async {
    final response = await _apiService.get('/tenant/$tenantId/leases/$leaseId');
    return LeaseModel.fromJson(response.data['data']);
  }

  // ============ Payments ============

  /// Get payment history
  @override
  Future<List<PaymentModel>> getPayments(
    int tenantId, {
    int pageNumber = 1,
    int pageSize = 20,
  }) async {
    final response = await _apiService.get(
      '/tenant/$tenantId/payments',
      queryParameters: {
        'pageNumber': pageNumber,
        'pageSize': pageSize,
      },
    );
    final List<dynamic> data = response.data['data']['payments'] ?? [];
    return data.map((e) => PaymentModel.fromJson(e)).toList();
  }

  /// Get payment details
  @override
  Future<PaymentModel> getPaymentDetails(int tenantId, int paymentId) async {
    final response = await _apiService.get('/tenant/$tenantId/payments/$paymentId');
    return PaymentModel.fromJson(response.data['data']);
  }

  /// Get pending invoices for payment
  @override
  Future<List<InvoiceModel>> getPendingInvoices(int tenantId) async {
    final response = await _apiService.get('/tenant/$tenantId/invoices/pending');
    final List<dynamic> data = response.data['data'] ?? [];
    return data.map((e) => InvoiceModel.fromJson(e)).toList();
  }

  /// Paginated invoice list with optional status filter.
  /// Pass `status = 'Outstanding'` to get Pending + Overdue together.
  @override
  Future<InvoiceListResult> getInvoices(
    int tenantId, {
    String? status,
    int pageNumber = 1,
    int pageSize = 20,
  }) async {
    final response = await _apiService.get(
      '/tenant/$tenantId/invoices',
      queryParameters: {
        if (status != null && status.isNotEmpty) 'status': status,
        'pageNumber': pageNumber,
        'pageSize': pageSize,
      },
    );
    return InvoiceListResult.fromJson(response.data['data'] as Map<String, dynamic>);
  }

  /// Full invoice detail (including the list of applied payments).
  @override
  Future<InvoiceDetailModel> getInvoiceDetails(int tenantId, int invoiceId) async {
    final response = await _apiService.get('/tenant/$tenantId/invoices/$invoiceId');
    return InvoiceDetailModel.fromJson(response.data['data'] as Map<String, dynamic>);
  }

  /// Upload one or more photos to a maintenance request. `filePaths` are
  /// local file paths from image_picker. `kind` is "Before" / "After" / "Other".
  @override
  Future<bool> uploadMaintenancePhotos({
    required int tenantId,
    required int requestId,
    required List<String> filePaths,
    String kind = 'Before',
  }) async {
    if (filePaths.isEmpty) return false;

    final form = FormData();
    for (final path in filePaths) {
      form.files.add(MapEntry(
        'photos',
        await MultipartFile.fromFile(path, filename: path.split('/').last),
      ),);
    }
    form.fields.add(MapEntry('kind', kind));

    final response = await _apiService.post(
      '/tenant/$tenantId/maintenance-requests/$requestId/photos',
      data: form,
    );
    return response.data['success'] == true;
  }

  /// Initiate KNET payment - returns redirect URL and payment details
  @override
  Future<KnetPaymentResponse> initiateKNETPayment(KnetPaymentRequest request) async {
    final response = await _apiService.post(
      '/payments/knet/initiate',
      data: request.toJson(),
    );
    return KnetPaymentResponse.fromJson(response.data);
  }

  /// Submit a bank transfer with the mandatory proof file. The backend
  /// requires a multipart form (BankTransferSubmitDto with an IFormFile),
  /// not JSON — and the route is `/payments/bank/upload` (baseUrl already
  /// ends in `/api`, so no leading `/api`). TenantId is derived from the
  /// JWT server-side; we still send it for the DTO binder.
  @override
  Future<bool> submitBankTransfer({
    required int tenantId,
    required int invoiceId,
    required double amount,
    required String bankName,
    required DateTime transferDate,
    required String proofFilePath,
    String? referenceNumber,
    String? notes,
  }) async {
    final form = FormData.fromMap({
      'TenantId': tenantId,
      'InvoiceId': invoiceId,
      'Amount': amount,
      'BankName': bankName,
      'TransferDate': transferDate.toIso8601String(),
      if (referenceNumber != null) 'ReferenceNumber': referenceNumber,
      if (notes != null) 'Notes': notes,
      'ProofFile': await MultipartFile.fromFile(
        proofFilePath,
        filename: proofFilePath.split('/').last,
      ),
    });

    final response = await _apiService.post(
      '/payments/bank/upload',
      data: form,
    );
    return response.data['success'] ?? false;
  }

  /// Tenant requests to pay an invoice in cash at the office. The server records
  /// a cash payment as "pending verification" and puts it in the office's
  /// approval queue — staff confirm it when the tenant pays at the counter.
  /// Returns true on success.
  /// [amount] is optional: null = the full outstanding balance. The server
  /// re-validates it against the invoice balance (0 < amount ≤ balance).
  Future<bool> requestCashPayment({
    required int invoiceId,
    double? amount,
    String? notes,
  }) async {
    final response = await _apiService.post(
      '/payments/cash/request',
      data: {
        'invoiceId': invoiceId,
        if (amount != null) 'amount': amount,
        if (notes != null && notes.isNotEmpty) 'notes': notes,
      },
    );
    return response.data['success'] ?? false;
  }

  // ============ Maintenance Requests ============

  /// Get maintenance requests
  @override
  Future<List<MaintenanceModel>> getMaintenanceRequests(
    int tenantId, {
    String? status,
  }) async {
    final response = await _apiService.get(
      '/tenant/$tenantId/maintenance-requests',
      queryParameters: status != null ? {'status': status} : null,
    );
    final List<dynamic> data = response.data['data'] ?? [];
    return data.map((e) => MaintenanceModel.fromJson(e)).toList();
  }

  /// Get maintenance request details
  @override
  Future<MaintenanceModel> getMaintenanceRequestDetails(
    int tenantId,
    int requestId,
  ) async {
    final response = await _apiService.get(
      '/tenant/$tenantId/maintenance-requests/$requestId',
    );
    return MaintenanceModel.fromJson(response.data['data']);
  }

  /// Create maintenance request
  @override
  Future<int> createMaintenanceRequest(
    int tenantId,
    CreateMaintenanceRequest request,
  ) async {
    final response = await _apiService.post(
      '/tenant/$tenantId/maintenance-requests',
      data: request.toJson(),
    );
    return response.data['data']['requestId'] ?? 0;
  }
}
