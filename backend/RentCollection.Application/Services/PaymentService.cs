using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;

namespace RentCollection.Application.Services;

/// <summary>
/// Base payment service implementation (SOLID: Dependency Inversion Principle)
/// Depends on abstractions (IRepository) not concrete implementations
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Payment> _paymentRepository;

    public PaymentService(IRepository<Payment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public async Task<Payment?> GetPaymentByIdAsync(int paymentId)
    {
        return await _paymentRepository.GetByIdAsync(paymentId);
    }

    public async Task<IEnumerable<Payment>> GetPaymentsByTenantAsync(int tenantId)
    {
        return await _paymentRepository.GetAllAsync(p => p.TenantId == tenantId);
    }

    public async Task<IEnumerable<Payment>> GetPaymentsByPropertyAsync(int propertyId)
    {
        return await _paymentRepository.GetAllAsync(p => p.PropertyId == propertyId);
    }

    public async Task<IEnumerable<Payment>> GetPaymentsByTypeAsync(PaymentType paymentType)
    {
        return await _paymentRepository.GetAllAsync(p => p.PaymentType == paymentType);
    }

    public async Task<decimal> GetTotalPaymentsByPropertyAsync(int propertyId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var payments = await _paymentRepository.GetAllAsync(p =>
            p.PropertyId == propertyId &&
            p.Status == PaymentStatus.Completed &&
            (!startDate.HasValue || p.PaymentDate >= startDate.Value) &&
            (!endDate.HasValue || p.PaymentDate <= endDate.Value));

        return payments.Sum(p => p.AmountPaid);
    }
}
