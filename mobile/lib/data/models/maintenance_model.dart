/// Maintenance request model
class MaintenanceModel {
  final int requestId;
  final String? propertyName;
  final String? propertyAddress;
  final String? unitNumber;
  final String category;
  final String priority;
  final String description;
  final String status;
  final DateTime requestDate;
  final DateTime? dueDate;       // backend Item 7: SLA clock derived from priority
  final DateTime? completedDate;
  final double? estimatedCost;
  final double? actualCost;
  final String? assignedTo;
  final String? notes;

  MaintenanceModel({
    required this.requestId,
    this.propertyName,
    this.propertyAddress,
    this.unitNumber,
    required this.category,
    required this.priority,
    required this.description,
    required this.status,
    required this.requestDate,
    this.dueDate,
    this.completedDate,
    this.estimatedCost,
    this.actualCost,
    this.assignedTo,
    this.notes,
  });

  /// True when dueDate has passed and the request is still open.
  /// Mirrors the backend's derived flag.
  bool get isOverdue {
    if (dueDate == null) return false;
    if (isCompleted || isCancelled) return false;
    return DateTime.now().isAfter(dueDate!);
  }

  /// Check if request is open
  bool get isOpen => status.toLowerCase() == 'open';

  /// Check if request is in progress
  bool get isInProgress => status.toLowerCase() == 'in progress';

  /// Check if request is completed
  bool get isCompleted => status.toLowerCase() == 'completed';

  /// Check if request is cancelled
  bool get isCancelled => status.toLowerCase() == 'cancelled';

  /// Get priority level (1=Low, 2=Medium, 3=High, 4=Emergency)
  int get priorityLevel {
    switch (priority.toLowerCase()) {
      case 'emergency':
        return 4;
      case 'high':
        return 3;
      case 'medium':
        return 2;
      case 'low':
      default:
        return 1;
    }
  }

  /// Get status color name for UI
  String get statusColorName {
    switch (status.toLowerCase()) {
      case 'open':
        return 'blue';
      case 'in progress':
        return 'orange';
      case 'completed':
        return 'green';
      case 'cancelled':
        return 'red';
      default:
        return 'grey';
    }
  }

  /// Get priority color name for UI
  String get priorityColorName {
    switch (priority.toLowerCase()) {
      case 'emergency':
        return 'red';
      case 'high':
        return 'orange';
      case 'medium':
        return 'yellow';
      case 'low':
      default:
        return 'green';
    }
  }

  /// Create MaintenanceModel from JSON
  factory MaintenanceModel.fromJson(Map<String, dynamic> json) {
    return MaintenanceModel(
      requestId: json['requestId'] ?? 0,
      propertyName: json['propertyName'],
      propertyAddress: json['propertyAddress'],
      unitNumber: json['unitNumber'],
      category: json['category'] ?? 'Other',
      priority: json['priority'] ?? 'Medium',
      description: json['description'] ?? '',
      status: json['status'] ?? 'Open',
      requestDate: DateTime.parse(
          json['requestDate'] ?? DateTime.now().toIso8601String()),
      dueDate: json['dueDate'] != null
          ? DateTime.tryParse(json['dueDate'])
          : null,
      completedDate: json['completedDate'] != null
          ? DateTime.tryParse(json['completedDate'])
          : null,
      estimatedCost: json['estimatedCost']?.toDouble(),
      actualCost: json['actualCost']?.toDouble(),
      assignedTo: json['assignedTo'],
      notes: json['notes'],
    );
  }

  /// Convert MaintenanceModel to JSON
  Map<String, dynamic> toJson() {
    return {
      'requestId': requestId,
      'propertyName': propertyName,
      'propertyAddress': propertyAddress,
      'unitNumber': unitNumber,
      'category': category,
      'priority': priority,
      'description': description,
      'status': status,
      'requestDate': requestDate.toIso8601String(),
      'completedDate': completedDate?.toIso8601String(),
      'estimatedCost': estimatedCost,
      'actualCost': actualCost,
      'assignedTo': assignedTo,
      'notes': notes,
    };
  }
}

/// Request model for creating a maintenance request
class CreateMaintenanceRequest {
  final int leaseId;
  final String category;
  final String priority;
  final String description;

  CreateMaintenanceRequest({
    required this.leaseId,
    required this.category,
    required this.priority,
    required this.description,
  });

  Map<String, dynamic> toJson() {
    return {
      'leaseId': leaseId,
      'category': category,
      'priority': priority,
      'description': description,
    };
  }
}
