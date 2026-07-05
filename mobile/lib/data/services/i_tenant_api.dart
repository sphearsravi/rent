import '../models/auth_model.dart';
import '../models/tenant_model.dart';
import '../models/lease_model.dart';
import '../models/payment_model.dart';
import '../models/maintenance_model.dart';
import '../models/dashboard_model.dart';
import '../models/invoice_detail_model.dart';
import '../models/invoice_list_result.dart';

/// Tenant-facing API contract. View-models should depend on this interface,
/// not on the concrete `TenantApiService`, so they can be tested with mock
/// implementations.
///
/// Signatures here mirror [TenantApiService] exactly — adding new methods?
/// Add them in both places, otherwise the mock surface drifts.
abstract class ITenantApi {
  Future<LoginResponse> login(LoginRequest request);
  Future<bool> changePassword(ChangePasswordRequest request);

  Future<TenantModel> getProfile(int tenantId);
  Future<bool> updateProfile(int tenantId, Map<String, dynamic> data);

  Future<DashboardModel> getDashboard(int tenantId);
  Future<List<NotificationModel>> getNotifications(int tenantId);

  Future<List<LeaseModel>> getLeases(int tenantId);
  Future<LeaseModel> getLeaseDetails(int tenantId, int leaseId);

  Future<List<PaymentModel>> getPayments(
    int tenantId, {
    int pageNumber = 1,
    int pageSize = 20,
  });
  Future<PaymentModel> getPaymentDetails(int tenantId, int paymentId);

  Future<List<InvoiceModel>> getPendingInvoices(int tenantId);
  Future<InvoiceListResult> getInvoices(
    int tenantId, {
    String? status,
    int pageNumber = 1,
    int pageSize = 20,
  });
  Future<InvoiceDetailModel> getInvoiceDetails(int tenantId, int invoiceId);
  Future<KnetPaymentResponse> initiateKNETPayment(KnetPaymentRequest request);
  Future<bool> submitBankTransfer({
    required int tenantId,
    required int invoiceId,
    required double amount,
    required String bankName,
    required DateTime transferDate,
    required String proofFilePath,
    String? referenceNumber,
    String? notes,
  });

  Future<List<MaintenanceModel>> getMaintenanceRequests(
    int tenantId, {
    String? status,
  });
  Future<MaintenanceModel> getMaintenanceRequestDetails(
    int tenantId,
    int requestId,
  );
  Future<int> createMaintenanceRequest(
    int tenantId,
    CreateMaintenanceRequest request,
  );

  /// Upload photos to an existing maintenance request. Returns true on
  /// server-reported success. See backend
  /// POST /api/tenant/{tenantId}/maintenance-requests/{requestId}/photos
  Future<bool> uploadMaintenancePhotos({
    required int tenantId,
    required int requestId,
    required List<String> filePaths,
    String kind = 'Before',
  });
}
