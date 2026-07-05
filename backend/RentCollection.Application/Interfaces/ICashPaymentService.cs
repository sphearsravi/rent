using RentCollection.Application.DTOs;

namespace RentCollection.Application.Interfaces;

/// <summary>
/// Cash payment service interface (SOLID: Single Responsibility Principle)
/// Handles only cash payment operations
/// </summary>
public interface ICashPaymentService
{
    Task<CashPaymentResponseDto> RecordCashPaymentAsync(CashPaymentDto cashPaymentDto, string collectedBy);
    Task<CashPaymentResponseDto?> GetCashPaymentByIdAsync(int cashPaymentId);
    Task<IEnumerable<CashPaymentResponseDto>> GetCashPaymentsByPropertyAsync(int propertyId, DateTime? startDate = null, DateTime? endDate = null);
    Task<IEnumerable<CashPaymentResponseDto>> GetCashPaymentsByDateAsync(DateTime date);
    // Receipt PDF generation removed — cash receipts now flow through the
    // shared IPdfService via GET /api/payments/{paymentId}/receipt.pdf,
    // which only serves Completed (i.e. approved) payments.
}
