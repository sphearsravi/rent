using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;

namespace RentCollection.Application.Services;

/// <summary>
/// Cash payment service implementation (SOLID: Single Responsibility + Dependency Inversion)
/// </summary>
public class CashPaymentService : ICashPaymentService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CashPayment> _cashPaymentRepository;
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<Tenant> _tenantRepository;
    private readonly IRepository<Property> _propertyRepository;

    public CashPaymentService(
        IRepository<Payment> paymentRepository,
        IRepository<CashPayment> cashPaymentRepository,
        IRepository<Invoice> invoiceRepository,
        IRepository<Tenant> tenantRepository,
        IRepository<Property> propertyRepository)
    {
        _paymentRepository = paymentRepository;
        _cashPaymentRepository = cashPaymentRepository;
        _invoiceRepository = invoiceRepository;
        _tenantRepository = tenantRepository;
        _propertyRepository = propertyRepository;
    }

    public async Task<CashPaymentResponseDto> RecordCashPaymentAsync(CashPaymentDto cashPaymentDto, string collectedBy)
    {
        // Validate invoice if provided and resolve the lease from it so the
        // cash Payment row participates in lease-level reconciliation just
        // like KNET / bank payments do.
        int leaseId = 0;
        if (cashPaymentDto.InvoiceId.HasValue)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(cashPaymentDto.InvoiceId.Value);
            if (invoice == null)
                throw new ArgumentException($"Invoice with ID {cashPaymentDto.InvoiceId} not found.");

            if (invoice.Status == "Paid")
                throw new InvalidOperationException($"Invoice {cashPaymentDto.InvoiceId} is already paid.");

            leaseId = invoice.LeaseId;
        }

        // Generate receipt number
        var receiptNumber = cashPaymentDto.ReceiptNumber ?? await GenerateReceiptNumberAsync();

        // Create payment record
        var payment = new Payment
        {
            InvoiceId = cashPaymentDto.InvoiceId,
            TenantId = cashPaymentDto.TenantId,
            PropertyId = cashPaymentDto.PropertyId,
            LeaseId = leaseId,
            PaymentDate = cashPaymentDto.PaymentDate,
            AmountPaid = cashPaymentDto.Amount,
            PaymentType = PaymentType.Cash,
            Status = PaymentStatus.PendingVerification,
            CollectedBy = collectedBy,
            ReceiptNumber = receiptNumber,
            Notes = cashPaymentDto.Notes,
            CreatedDate = DateTime.UtcNow
        };

        var createdPayment = await _paymentRepository.AddAsync(payment);

        // Create cash payment record
        var cashPayment = new CashPayment
        {
            PaymentId = createdPayment.PaymentId,
            TenantId = cashPaymentDto.TenantId,
            PropertyId = cashPaymentDto.PropertyId,
            InvoiceId = cashPaymentDto.InvoiceId,
            Amount = cashPaymentDto.Amount,
            PaymentDate = cashPaymentDto.PaymentDate,
            CollectedBy = collectedBy,
            ReceiptNumber = receiptNumber,
            Notes = cashPaymentDto.Notes,
            CreatedBy = collectedBy,
            CreatedDate = DateTime.UtcNow
        };

        var createdCashPayment = await _cashPaymentRepository.AddAsync(cashPayment);

        // Update invoice status to pending verification if applicable
        // Invoice will be marked as Paid when the payment is verified/approved
        if (cashPaymentDto.InvoiceId.HasValue)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(cashPaymentDto.InvoiceId.Value);
            if (invoice != null)
            {
                invoice.Status = "Pending Verification";
                await _invoiceRepository.UpdateAsync(invoice);
            }
        }

        // Get tenant and property names
        var tenant = await _tenantRepository.GetByIdAsync(cashPaymentDto.TenantId);
        var property = await _propertyRepository.GetByIdAsync(cashPaymentDto.PropertyId);

        return new CashPaymentResponseDto
        {
            PaymentId = createdPayment.PaymentId,
            CashPaymentId = createdCashPayment.CashPaymentId,
            ReceiptNumber = receiptNumber,
            Amount = cashPaymentDto.Amount,
            PaymentDate = cashPaymentDto.PaymentDate,
            Status = PaymentStatus.PendingVerification.ToString(),
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
            PropertyName = property?.PropertyName ?? "Unknown"
        };
    }

    public async Task<CashPaymentResponseDto?> GetCashPaymentByIdAsync(int cashPaymentId)
    {
        var cashPayment = await _cashPaymentRepository.GetByIdAsync(cashPaymentId);
        if (cashPayment == null) return null;

        var payment = await _paymentRepository.GetByIdAsync(cashPayment.PaymentId);
        var tenant = await _tenantRepository.GetByIdAsync(cashPayment.TenantId);
        var property = await _propertyRepository.GetByIdAsync(cashPayment.PropertyId);

        return new CashPaymentResponseDto
        {
            PaymentId = cashPayment.PaymentId,
            CashPaymentId = cashPayment.CashPaymentId,
            ReceiptNumber = cashPayment.ReceiptNumber ?? "",
            Amount = cashPayment.Amount,
            PaymentDate = cashPayment.PaymentDate,
            Status = payment?.Status.ToString() ?? "Unknown",
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
            PropertyName = property?.PropertyName ?? "Unknown"
        };
    }

    public async Task<IEnumerable<CashPaymentResponseDto>> GetCashPaymentsByPropertyAsync(int propertyId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var cashPayments = await _cashPaymentRepository.GetAllAsync(cp =>
            cp.PropertyId == propertyId &&
            (!startDate.HasValue || cp.PaymentDate >= startDate.Value) &&
            (!endDate.HasValue || cp.PaymentDate <= endDate.Value));

        var result = new List<CashPaymentResponseDto>();
        foreach (var cp in cashPayments)
        {
            var payment = await _paymentRepository.GetByIdAsync(cp.PaymentId);
            var tenant = await _tenantRepository.GetByIdAsync(cp.TenantId);
            var property = await _propertyRepository.GetByIdAsync(cp.PropertyId);

            result.Add(new CashPaymentResponseDto
            {
                PaymentId = cp.PaymentId,
                CashPaymentId = cp.CashPaymentId,
                ReceiptNumber = cp.ReceiptNumber ?? "",
                Amount = cp.Amount,
                PaymentDate = cp.PaymentDate,
                Status = payment?.Status.ToString() ?? "Unknown",
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
                PropertyName = property?.PropertyName ?? "Unknown"
            });
        }

        return result;
    }

    public async Task<IEnumerable<CashPaymentResponseDto>> GetCashPaymentsByDateAsync(DateTime date)
    {
        var cashPayments = await _cashPaymentRepository.GetAllAsync(cp =>
            cp.PaymentDate.Date == date.Date);

        var result = new List<CashPaymentResponseDto>();
        foreach (var cp in cashPayments)
        {
            var payment = await _paymentRepository.GetByIdAsync(cp.PaymentId);
            var tenant = await _tenantRepository.GetByIdAsync(cp.TenantId);
            var property = await _propertyRepository.GetByIdAsync(cp.PropertyId);

            result.Add(new CashPaymentResponseDto
            {
                PaymentId = cp.PaymentId,
                CashPaymentId = cp.CashPaymentId,
                ReceiptNumber = cp.ReceiptNumber ?? "",
                Amount = cp.Amount,
                PaymentDate = cp.PaymentDate,
                Status = payment?.Status.ToString() ?? "Unknown",
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "Unknown",
                PropertyName = property?.PropertyName ?? "Unknown"
            });
        }

        return result;
    }

    private async Task<string> GenerateReceiptNumberAsync()
    {
        var count = await _cashPaymentRepository.CountAsync();
        return $"CASH-{DateTime.Now:yyyyMMdd}-{(count + 1):D5}";
    }
}
