import 'payment_model.dart' show InvoiceModel;

/// Paginated invoice list returned by
/// GET /api/tenant/{tenantId}/invoices?status=&pageNumber=&pageSize=
///
/// Backend wraps the array in `data: { totalCount, pageNumber, pageSize,
/// invoices: [...] }`. This model owns that envelope so the view-model
/// doesn't have to know about pagination shape.
class InvoiceListResult {
  final int totalCount;
  final int pageNumber;
  final int pageSize;
  final List<InvoiceModel> invoices;

  InvoiceListResult({
    required this.totalCount,
    required this.pageNumber,
    required this.pageSize,
    required this.invoices,
  });

  bool get hasMore => pageNumber * pageSize < totalCount;

  factory InvoiceListResult.fromJson(Map<String, dynamic> json) {
    final list = (json['invoices'] as List<dynamic>? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(InvoiceModel.fromJson)
        .toList();
    return InvoiceListResult(
      totalCount: (json['totalCount'] as num?)?.toInt() ?? list.length,
      pageNumber: (json['pageNumber'] as num?)?.toInt() ?? 1,
      pageSize: (json['pageSize'] as num?)?.toInt() ?? list.length,
      invoices: list,
    );
  }
}
