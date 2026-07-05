using RentCollection.Core.Entities;
using RentCollection.Core.Enums;

namespace RentCollection.Application.Interfaces;

/// <summary>
/// Base payment service interface (SOLID: Interface Segregation Principle)
/// </summary>
public interface IPaymentService
{
    Task<Payment?> GetPaymentByIdAsync(int paymentId);
    Task<IEnumerable<Payment>> GetPaymentsByTenantAsync(int tenantId);
    Task<IEnumerable<Payment>> GetPaymentsByPropertyAsync(int propertyId);
    Task<IEnumerable<Payment>> GetPaymentsByTypeAsync(PaymentType paymentType);
    Task<decimal> GetTotalPaymentsByPropertyAsync(int propertyId, DateTime? startDate = null, DateTime? endDate = null);
}
