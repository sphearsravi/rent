using System.Security.Cryptography;
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
        // KWD is 3-decimal (fils); round once and use `amount` throughout.
        var amount = decimal.Round(cashPaymentDto.Amount, 3);
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");

        int leaseId = 0;
        if (cashPaymentDto.InvoiceId.HasValue)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(cashPaymentDto.InvoiceId.Value);
            if (invoice == null)
                throw new ArgumentException($"Invoice with ID {cashPaymentDto.InvoiceId} not found.");

            if (invoice.Status == InvoiceStatuses.Paid)
                throw new InvalidOperationException($"Invoice {cashPaymentDto.InvoiceId} is already paid.");

            if (invoice.Status == InvoiceStatuses.Cancelled)
                throw new InvalidOperationException($"Invoice {cashPaymentDto.InvoiceId} is cancelled and cannot be paid.");

            // Never record more than is owed — approval would otherwise push
            // PaidAmount past TotalAmount.
            var balance = decimal.Round(invoice.TotalAmount - invoice.PaidAmount, 3);
            if (amount > balance)
                throw new ArgumentException(
                    $"Amount {amount:0.000} KWD exceeds the outstanding balance of {balance:0.000} KWD on invoice {cashPaymentDto.InvoiceId}.");

            leaseId = invoice.LeaseId;
        }

        // Receipt number is ALWAYS generated server-side. The DTO's
        // ReceiptNumber was previously trusted verbatim, which let a caller
        // forge or duplicate receipt numbers; it is now ignored.
        var receiptNumber = await GenerateReceiptNumberAsync();

        // Create payment record
        var payment = new Payment
        {
            InvoiceId = cashPaymentDto.InvoiceId,
            TenantId = cashPaymentDto.TenantId,
            PropertyId = cashPaymentDto.PropertyId,
            LeaseId = leaseId,
            PaymentDate = cashPaymentDto.PaymentDate,
            AmountPaid = amount,
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
            Amount = amount,
            PaymentDate = cashPaymentDto.PaymentDate,
            CollectedBy = collectedBy,
            ReceiptNumber = receiptNumber,
            ManualReceiptNumber = string.IsNullOrWhiteSpace(cashPaymentDto.ManualReceiptNumber)
                ? null
                : cashPaymentDto.ManualReceiptNumber.Trim()[..Math.Min(50, cashPaymentDto.ManualReceiptNumber.Trim().Length)],
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
                invoice.Status = InvoiceStatuses.PendingVerification;
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
            Amount = amount,
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

    /// <summary>
    /// Server-issued cash receipt number: <c>CASH-yyyyMMdd-XXXXXX</c>.
    /// The old <c>COUNT(*)+1</c> scheme collided whenever two cashiers recorded
    /// within the same request window (both read the same count) and used
    /// server-local time for the date. Now: UTC date + a 6-char random base-36
    /// suffix (≈2.2 billion per day), re-drawn until no CashPayment OR Payment
    /// already carries it.
    /// </summary>
    private async Task<string> GenerateReceiptNumberAsync()
    {
        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = $"CASH-{DateTime.UtcNow:yyyyMMdd}-{RandomBase36(6)}";
            var taken = await _cashPaymentRepository.ExistsAsync(cp => cp.ReceiptNumber == candidate)
                     || await _paymentRepository.ExistsAsync(p => p.ReceiptNumber == candidate);
            if (!taken) return candidate;
        }
        throw new InvalidOperationException("Could not allocate a unique cash receipt number; please retry.");
    }

    private static string RandomBase36(int length)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return string.Create(length, alphabet, (span, a) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = a[RandomNumberGenerator.GetInt32(a.Length)];
        });
    }
}
