import 'tenant_model.dart';
import 'lease_model.dart';
import 'payment_model.dart';
import 'maintenance_model.dart';

/// Dashboard statistics model
class DashboardStatistics {
  final int activeLeasesCount;
  final int totalPaymentsCount;
  final int pendingMaintenanceRequestsCount;

  DashboardStatistics({
    this.activeLeasesCount = 0,
    this.totalPaymentsCount = 0,
    this.pendingMaintenanceRequestsCount = 0,
  });

  factory DashboardStatistics.fromJson(Map<String, dynamic> json) {
    return DashboardStatistics(
      activeLeasesCount: json['activeLeasesCount'] ?? 0,
      totalPaymentsCount: json['totalPaymentsCount'] ?? 0,
      pendingMaintenanceRequestsCount: json['pendingMaintenanceRequestsCount'] ?? 0,
    );
  }
}

/// Dashboard model containing all dashboard data
class DashboardModel {
  final TenantModel? tenant;
  final DashboardStatistics statistics;
  final List<LeaseModel> activeLeases;
  final List<PaymentModel> recentPayments;
  final List<MaintenanceModel> maintenanceRequests;

  DashboardModel({
    this.tenant,
    required this.statistics,
    this.activeLeases = const [],
    this.recentPayments = const [],
    this.maintenanceRequests = const [],
  });

  /// Total monthly rent across all active leases
  double get totalMonthlyRent {
    return activeLeases.fold(0.0, (sum, lease) => sum + lease.monthlyRent);
  }

  /// Check if tenant has any active leases
  bool get hasActiveLeases => activeLeases.isNotEmpty;

  /// Check if tenant has pending maintenance requests
  bool get hasPendingMaintenance => maintenanceRequests.isNotEmpty;

  factory DashboardModel.fromJson(Map<String, dynamic> json) {
    return DashboardModel(
      tenant: json['tenant'] != null
          ? TenantModel.fromJson(json['tenant'])
          : null,
      statistics: json['statistics'] != null
          ? DashboardStatistics.fromJson(json['statistics'])
          : DashboardStatistics(),
      activeLeases: (json['activeLeases'] as List<dynamic>?)
              ?.map((e) => LeaseModel.fromJson(e))
              .toList() ??
          [],
      recentPayments: (json['recentPayments'] as List<dynamic>?)
              ?.map((e) => PaymentModel.fromJson(e))
              .toList() ??
          [],
      maintenanceRequests: (json['maintenanceRequests'] as List<dynamic>?)
              ?.map((e) => MaintenanceModel.fromJson(e))
              .toList() ??
          [],
    );
  }
}

/// Notification model for tenant notifications
class NotificationModel {
  final String type;
  final String title;
  final String message;
  final DateTime? date;
  final String priority;

  NotificationModel({
    required this.type,
    required this.title,
    required this.message,
    this.date,
    this.priority = 'low',
  });

  /// Check if notification is high priority
  bool get isHighPriority => priority.toLowerCase() == 'high';

  /// Check if notification is payment related
  bool get isPaymentRelated => type.toLowerCase().contains('payment');

  /// Check if notification is maintenance related
  bool get isMaintenanceRelated => type.toLowerCase().contains('maintenance');

  factory NotificationModel.fromJson(Map<String, dynamic> json) {
    return NotificationModel(
      type: json['type'] ?? '',
      title: json['title'] ?? '',
      message: json['message'] ?? '',
      date: json['date'] != null ? DateTime.tryParse(json['date']) : null,
      priority: json['priority'] ?? 'low',
    );
  }
}
