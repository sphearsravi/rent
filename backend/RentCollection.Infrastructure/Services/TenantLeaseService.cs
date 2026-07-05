using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Service for handling multi-lease tenant operations
/// </summary>
public class TenantLeaseService : ITenantLeaseService
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<TenantLeaseService> _logger;

    public TenantLeaseService(
        ApplicationDbContext context,
        IJwtTokenService jwtTokenService,
        ILogger<TenantLeaseService> logger)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task<TenantLoginResponseDto> LoginByCivilIdAsync(TenantLoginRequestDto request)
    {
        try
        {
            _logger.LogInformation("Login attempt for Civil ID: {CivilID}", request.CivilID);

            // Find tenant by Civil ID (GovernmentId field)
            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.GovernmentId == request.CivilID && t.Status == "Active");

            if (tenant == null)
            {
                _logger.LogWarning("Tenant not found for Civil ID: {CivilID}", request.CivilID);
                return new TenantLoginResponseDto
                {
                    Success = false,
                    ErrorMessage = "Invalid Civil ID or account is inactive."
                };
            }

            // Verify password (using BCrypt)
            if (string.IsNullOrEmpty(tenant.Password) ||
                !BCrypt.Net.BCrypt.Verify(request.Password, tenant.Password))
            {
                _logger.LogWarning("Invalid password for Civil ID: {CivilID}", request.CivilID);
                return new TenantLoginResponseDto
                {
                    Success = false,
                    ErrorMessage = "Invalid Civil ID or password."
                };
            }

            // Get all active leases for this tenant
            var activeLeases = await GetActiveLeasesByCivilIdAsync(request.CivilID);

            // Generate JWT token
            var token = _jwtTokenService.GenerateToken(tenant.TenantId, tenant.Email ?? request.CivilID);

            _logger.LogInformation("Successful login for Civil ID: {CivilID}, Found {LeaseCount} active leases",
                request.CivilID, activeLeases.Count);

            return new TenantLoginResponseDto
            {
                Success = true,
                Token = token,
                Tenant = new TenantInfoDto
                {
                    TenantId = tenant.TenantId,
                    CivilID = tenant.GovernmentId ?? string.Empty,
                    FullName = $"{tenant.FirstName} {tenant.LastName}".Trim(),
                    Email = tenant.Email,
                    Phone = tenant.Phone
                },
                ActiveLeases = activeLeases
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for Civil ID: {CivilID}", request.CivilID);
            return new TenantLoginResponseDto
            {
                Success = false,
                ErrorMessage = "An error occurred during login."
            };
        }
    }

    public async Task<List<LeaseInfoDto>> GetActiveLeasesByCivilIdAsync(string civilId)
    {
        var leases = await _context.Leases
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Include(l => l.Tenant)
            .Include(l => l.Invoices)
            .Where(l => l.Tenant.GovernmentId == civilId && l.Status == "Active")
            .Select(l => new LeaseInfoDto
            {
                LeaseId = l.LeaseId,
                UnitId = l.UnitId,
                UnitNumber = l.Unit.UnitNumber,
                PropertyId = l.Unit.PropertyId,
                BuildingName = l.Unit.Property.PropertyName,
                BuildingAddress = l.Unit.Property.Address,
                BuildingPACINumber = l.Unit.Property.PACINumber,
                LeaseStartDate = l.LeaseStartDate,
                LeaseEndDate = l.LeaseEndDate,
                MonthlyRent = l.MonthlyRent,
                Status = l.Status,
                AcceptKNET = l.Unit.Property.AcceptKNET,
                KNETAccountID = l.Unit.Property.KNETAccountID,
                KNETMID = l.Unit.Property.KNETMID,
                TotalOutstanding = l.Invoices
                    .Where(i => i.Status != "Paid")
                    .Sum(i => i.TotalAmount - i.PaidAmount),
                PendingInvoicesCount = l.Invoices
                    .Count(i => i.Status != "Paid")
            })
            .OrderBy(l => l.BuildingName)
            .ThenBy(l => l.UnitNumber)
            .ToListAsync();

        return leases;
    }

    public async Task<LeasePaymentInfoDto?> GetLeasePaymentInfoAsync(int leaseId, string civilId)
    {
        var lease = await _context.Leases
            .Include(l => l.Unit)
                .ThenInclude(u => u.Property)
            .Include(l => l.Tenant)
            .Include(l => l.Invoices)
            .FirstOrDefaultAsync(l => l.LeaseId == leaseId && l.Tenant.GovernmentId == civilId);

        if (lease == null)
        {
            _logger.LogWarning("Lease {LeaseId} not found for Civil ID: {CivilID}", leaseId, civilId);
            return null;
        }

        var pendingInvoices = await GetPendingInvoicesForLeaseAsync(leaseId);

        return new LeasePaymentInfoDto
        {
            LeaseId = lease.LeaseId,
            TenantId = lease.TenantId,
            CivilID = civilId,
            PropertyId = lease.Unit.PropertyId,
            BuildingName = lease.Unit.Property.PropertyName,
            UnitNumber = lease.Unit.UnitNumber,
            MonthlyRent = lease.MonthlyRent,
            KNETAccountID = lease.Unit.Property.KNETAccountID,
            KNETMID = lease.Unit.Property.KNETMID,
            KnetTranportalId = lease.Unit.Property.KnetTranportalId,
            KnetTranportalPwd = lease.Unit.Property.KnetTranportalPwd,
            KnetTerminalResourceKey = lease.Unit.Property.KnetTerminalResourceKey,
            PendingInvoices = pendingInvoices
        };
    }

    public async Task<List<InvoiceInfoDto>> GetPendingInvoicesForLeaseAsync(int leaseId)
    {
        return await _context.Invoices
            .Where(i => i.LeaseId == leaseId && i.Status != "Paid")
            .OrderBy(i => i.DueDate)
            .Select(i => new InvoiceInfoDto
            {
                InvoiceId = i.InvoiceId,
                LeaseId = i.LeaseId,
                InvoiceDate = i.InvoiceDate,
                DueDate = i.DueDate,
                TotalAmount = i.TotalAmount,
                PaidAmount = i.PaidAmount,
                Status = i.Status,
                InvoiceType = i.InvoiceType
            })
            .ToListAsync();
    }

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateCivilIdForLeaseAsync(string civilId, int leaseId)
    {
        var lease = await _context.Leases
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LeaseId == leaseId);

        if (lease == null)
        {
            return (false, "Lease not found.");
        }

        if (lease.Tenant.GovernmentId != civilId)
        {
            _logger.LogWarning("Civil ID mismatch: Expected {Expected}, Got {Actual} for Lease {LeaseId}",
                lease.Tenant.GovernmentId, civilId, leaseId);
            return (false, $"Civil ID does not match the lease owner. Expected: {lease.Tenant.GovernmentId}");
        }

        if (lease.Status != "Active")
        {
            return (false, "Lease is not active.");
        }

        return (true, null);
    }

    public async Task<PaymentReceiptDto?> GetPaymentReceiptAsync(int paymentId)
    {
        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.Lease)
                .ThenInclude(l => l.Unit)
                    .ThenInclude(u => u.Property)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

        if (payment == null)
        {
            return null;
        }

        return new PaymentReceiptDto
        {
            PaymentId = payment.PaymentId,
            ReceiptNumber = payment.ReceiptNumber ?? $"RCPT-{payment.PaymentId:D8}",
            PaymentDate = payment.PaymentDate,
            CivilID = payment.CivilID ?? payment.Tenant.GovernmentId ?? string.Empty,
            TenantName = $"{payment.Tenant.FirstName} {payment.Tenant.LastName}".Trim(),
            LeaseId = payment.LeaseId,
            LeaseReference = $"LEASE-{payment.LeaseId:D6}",
            PropertyId = payment.PropertyId,
            BuildingName = payment.Lease?.Unit?.Property?.PropertyName ?? string.Empty,
            UnitNumber = payment.Lease?.Unit?.UnitNumber ?? string.Empty,
            RentAmount = payment.AmountPaid - (payment.LateFeeAmount ?? 0),
            LateFeeAmount = payment.LateFeeAmount ?? 0,
            TotalAmount = payment.AmountPaid,
            PaymentMethod = payment.PaymentType.ToString(),
            TransactionId = payment.TransactionReference,
            CheckoutPaymentId = payment.CheckoutPaymentId,
            KNETAccountID = payment.KNETAccountIDUsed,
            KNETMID = payment.KNETMIDUsed,
            Status = payment.Status.ToString()
        };
    }
}
