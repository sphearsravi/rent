using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Tests.Helpers;
using RentCollection.Web.Controllers;
using RentCollection.Web.Controllers.Api;

namespace RentCollection.Tests.Payments;

/// <summary>
/// Cash flow, both ends:
///  - tenant side: POST api/payments/cash/request (optional partial amount,
///    receipt number withheld until confirmed)
///  - office side: PaymentApproval Approve/Reject (two-person control with
///    SuperAdmin override, tenant notified of the outcome)
/// </summary>
public class CashRequestAndApprovalTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IPaymentEligibilityService> _eligibility = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ICashPaymentService> _cashService = new();

    public CashRequestAndApprovalTests()
    {
        _context = TestDatabaseHelper.CreateInMemoryContext(Guid.NewGuid().ToString());
        _eligibility.Setup(e => e.CheckAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EligibilityResult.Eligible());
        _notifications.Setup(n => n.EnqueueTenantPushAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    public void Dispose() => _context.Dispose();

    // ---------------------------------------------------------------- helpers

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    private static void Attach(Controller controller, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
    }

    private static void Attach(ControllerBase controller, ClaimsPrincipal user)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    private CashRequestApiController TenantController(int tenantId)
    {
        var c = new CashRequestApiController(
            _cashService.Object, _eligibility.Object, _context,
            Mock.Of<ILogger<CashRequestApiController>>());
        Attach(c, Principal(new Claim("tenantId", tenantId.ToString())));
        return c;
    }

    private PaymentApprovalController OfficeController(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName), new("companyId", "1") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var c = new PaymentApprovalController(
            _context, _eligibility.Object, _notifications.Object,
            Mock.Of<ILogger<PaymentApprovalController>>());
        Attach(c, Principal(claims.ToArray()));
        return c;
    }

    /// <summary>Pending cash payment on invoice 1 (450 KWD, overdue), recorded by "clerk".</summary>
    private async Task<Payment> SeedPendingCashAsync(string collectedBy = "clerk", decimal amount = 450m)
    {
        var payment = new Payment
        {
            PaymentId = 50,
            LeaseId = 1,
            TenantId = 1,
            PropertyId = 1,
            InvoiceId = 1,
            PaymentDate = DateTime.UtcNow,
            AmountPaid = amount,
            PaymentType = PaymentType.Cash,
            Status = PaymentStatus.PendingVerification,
            CollectedBy = collectedBy,
            ReceiptNumber = "CASH-TEST-0001",
            CreatedDate = DateTime.UtcNow
        };
        _context.Payments.Add(payment);
        _context.CashPayments.Add(new CashPayment
        {
            CashPaymentId = 1,
            PaymentId = 50,
            TenantId = 1,
            PropertyId = 1,
            InvoiceId = 1,
            Amount = amount,
            PaymentDate = DateTime.UtcNow,
            CollectedBy = collectedBy,
            CreatedBy = collectedBy,
            ReceiptNumber = "CASH-TEST-0001"
        });
        var invoice = await _context.Invoices.FirstAsync(i => i.InvoiceId == 1);
        invoice.Status = InvoiceStatuses.PendingVerification;
        await _context.SaveChangesAsync();
        return payment;
    }

    private CashPaymentDto? _recordedDto;

    private void StubCashService()
    {
        _cashService
            .Setup(s => s.RecordCashPaymentAsync(It.IsAny<CashPaymentDto>(), It.IsAny<string>()))
            .ReturnsAsync((CashPaymentDto dto, string _) =>
            {
                _recordedDto = dto;
                return new CashPaymentResponseDto
                {
                    PaymentId = 99,
                    CashPaymentId = 9,
                    ReceiptNumber = "CASH-SECRET-9999",
                    Amount = dto.Amount,
                    PaymentDate = dto.PaymentDate,
                    Status = "PendingVerification"
                };
            });
    }

    private static string Json(IActionResult result) =>
        JsonSerializer.Serialize(((ObjectResult)result).Value);

    // ------------------------------------------------ tenant: cash request

    [Fact]
    public async Task Request_WithoutAmount_UsesFullBalance_AndHidesReceipt()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        StubCashService();

        var result = await TenantController(1).RequestCash(
            new CashRequestApiController.CashRequestDto { InvoiceId = 1 });

        result.Should().BeOfType<OkObjectResult>();
        _recordedDto!.Amount.Should().Be(450m);
        _recordedDto.TenantId.Should().Be(1);
        _recordedDto.PropertyId.Should().Be(1);

        var json = Json(result);
        json.Should().NotContainEquivalentOf("receipt", "the receipt is only issued once the office confirms");
        json.Should().Contain("\"isPartial\":false");
    }

    [Fact]
    public async Task Request_WithPartialAmount_RecordsThatAmount_AndFlagsPartial()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        StubCashService();

        var result = await TenantController(1).RequestCash(
            new CashRequestApiController.CashRequestDto { InvoiceId = 1, Amount = 100.5m, Notes = "rest next week" });

        result.Should().BeOfType<OkObjectResult>();
        _recordedDto!.Amount.Should().Be(100.5m);
        _recordedDto.Notes.Should().Contain("PARTIAL").And.Contain("100.500").And.Contain("rest next week");
        Json(result).Should().Contain("\"isPartial\":true");

        var audit = await _context.PaymentAudits.SingleAsync(a => a.PaymentId == 99);
        audit.AuditType.Should().Be("CashRequestedByTenant");
        audit.Notes.Should().Contain("partial");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(450.001)]
    [InlineData(1000)]
    public async Task Request_WithAmountOutsideBalance_IsRejected(decimal amount)
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        StubCashService();

        var result = await TenantController(1).RequestCash(
            new CashRequestApiController.CashRequestDto { InvoiceId = 1, Amount = amount });

        result.Should().BeOfType<BadRequestObjectResult>();
        _recordedDto.Should().BeNull("nothing may be recorded for an invalid amount");
        Json(result).Should().Contain("450.000");
    }

    [Fact]
    public async Task Request_ForAnotherTenantsInvoice_IsForbidden()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        StubCashService();

        // Invoice 1 belongs to tenant 1; caller is tenant 2.
        var result = await TenantController(2).RequestCash(
            new CashRequestApiController.CashRequestDto { InvoiceId = 1 });

        result.Should().BeOfType<ForbidResult>();
        _recordedDto.Should().BeNull();
    }

    // --------------------------------------------- office: approve / reject

    [Fact]
    public async Task Approve_ByTheRecorder_IsBlocked_ForNonSuperAdmin()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedPendingCashAsync(collectedBy: "clerk");

        var result = await OfficeController("clerk", Roles.Admin).Approve(50);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Details");
        (await _context.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == 50))
            .Status.Should().Be(PaymentStatus.PendingVerification);
        _notifications.Verify(n => n.EnqueueTenantPushAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_ByTheRecorder_AsSuperAdmin_IsAllowed_AndStamped()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedPendingCashAsync(collectedBy: "owner");

        var result = await OfficeController("owner", Roles.SuperAdmin).Approve(50);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");

        var payment = await _context.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == 50);
        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.ApprovedBy.Should().Be("owner");
        payment.Notes.Should().Contain("SELF-APPROVED").And.Contain("SuperAdmin override");

        var audit = await _context.PaymentAudits.SingleAsync(a => a.PaymentId == 50 && a.AuditType == "Approved");
        audit.Notes.Should().Contain("SELF-APPROVED");
    }

    [Fact]
    public async Task Approve_ByAnotherUser_CompletesPayment_PaysInvoice_AndNotifiesTenant()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedPendingCashAsync(collectedBy: "clerk");

        var result = await OfficeController("manager", Roles.Admin).Approve(50);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");

        var payment = await _context.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == 50);
        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.Notes.Should().NotContain("SELF-APPROVED");

        var invoice = await _context.Invoices.AsNoTracking().SingleAsync(i => i.InvoiceId == 1);
        invoice.PaidAmount.Should().Be(450m);
        invoice.Status.Should().Be(InvoiceStatuses.Paid);

        // Push to the tenant's devices, with a deep-link payload …
        _notifications.Verify(n => n.EnqueueTenantPushAsync(
            1,
            "Payment confirmed",
            It.Is<string>(b => b.Contains("450.000") && b.Contains("invoice #1") && b.Contains("CASH-TEST-0001")),
            It.Is<IDictionary<string, string>>(d => d["type"] == "payment_approved" && d["paymentId"] == "50" && d["invoiceId"] == "1"),
            "Payment", 50, It.IsAny<CancellationToken>()), Times.Once);

        // … and an email, because seeded tenant 1 has an address.
        _notifications.Verify(n => n.EnqueueTenantAsync(
            1, "Payment confirmed", It.IsAny<string>(), "Email", "Payment", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_TenantWithoutEmail_SkipsEmail_StillPushes()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedPendingCashAsync(collectedBy: "clerk");
        var tenant = await _context.Tenants.FirstAsync(t => t.TenantId == 1);
        tenant.Email = string.Empty;
        await _context.SaveChangesAsync();

        await OfficeController("manager", Roles.Admin).Approve(50);

        _notifications.Verify(n => n.EnqueueTenantPushAsync(
            1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(),
            "Payment", 50, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.EnqueueTenantAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_NotificationFailure_DoesNotUndoTheApproval()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedPendingCashAsync(collectedBy: "clerk");
        _notifications.Setup(n => n.EnqueueTenantPushAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("FCM down"));

        var result = await OfficeController("manager", Roles.Admin).Approve(50);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
        (await _context.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == 50))
            .Status.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    public async Task Reject_RevertsInvoice_AndNotifiesTenantWithReason()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedPendingCashAsync(collectedBy: "clerk");

        var result = await OfficeController("manager", Roles.Admin).Reject(50, "Tenant did not show up");

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");

        var payment = await _context.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == 50);
        payment.Status.Should().Be(PaymentStatus.Rejected);

        // Invoice 1 was overdue before the request → back to Overdue, not blindly Pending.
        var invoice = await _context.Invoices.AsNoTracking().SingleAsync(i => i.InvoiceId == 1);
        invoice.Status.Should().Be(InvoiceStatuses.Overdue);
        invoice.PaidAmount.Should().Be(0m);

        _notifications.Verify(n => n.EnqueueTenantPushAsync(
            1,
            "Payment not confirmed",
            It.Is<string>(b => b.Contains("Tenant did not show up")),
            It.Is<IDictionary<string, string>>(d => d["type"] == "payment_rejected"),
            "Payment", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_WithoutReason_IsRefused_AndNothingIsSent()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedPendingCashAsync(collectedBy: "clerk");

        var result = await OfficeController("manager", Roles.Admin).Reject(50, "   ");

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Details");
        (await _context.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == 50))
            .Status.Should().Be(PaymentStatus.PendingVerification);
        _notifications.VerifyNoOtherCalls();
    }
}
