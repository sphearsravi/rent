using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Knet;

/// <summary>
/// Sprint 7 Tier 2 — money math and state-machine invariants for the K-NET
/// payment surface. The Tier 1 tests prove single-callback correctness;
/// these prove invariants across multiple payments and state transitions.
///
/// Cardinal rules pinned here:
///   • An invoice's PaidAmount never exceeds its TotalAmount
///   • A payment never transitions backwards (Completed → Pending)
///   • Partial payments compose: two 250 + one 500 callback = invoice paid
///   • Receipt numbers are stable once issued (CAPTURED stamps them once)
/// </summary>
public class PaymentMoneyMathTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly KnetPaymentService _sut;

    public PaymentMoneyMathTests()
    {
        _ctx = TestDatabaseHelper.CreateInMemoryContextWithTransactionsIgnored($"Money_{Guid.NewGuid()}");
        var settings = Options.Create(new KnetSettings { TerminalResourceKey = "0123456789ABCDEF" });
        _sut = new KnetPaymentService(
            new TestRepo<Payment>(_ctx), new TestRepo<Invoice>(_ctx),
            _ctx, settings, new KnetUtil(),
            NullLogger<KnetPaymentService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public async Task Partial_payment_leaves_invoice_PartiallyPaid_with_correct_balance()
    {
        await SeedInvoiceAsync(invoiceId: 1, total: 500m);
        await SeedPendingPaymentAsync(paymentId: 1, invoiceId: 1, amount: 200m, trackId: "TRK_P1");

        await _sut.HandleKnetResponseAsync(null, "KP_P1", "TRK_P1", "TX_1", "CAPTURED", null);

        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync();
        invoice.Status.Should().Be("Partially Paid",
            "200 KWD against a 500 KWD invoice is partial, not Paid");
        invoice.PaidAmount.Should().Be(200m);
        (invoice.TotalAmount - invoice.PaidAmount).Should().Be(300m, "outstanding balance");
    }

    [Fact]
    public async Task Two_partial_payments_summing_to_total_flip_invoice_to_Paid()
    {
        await SeedInvoiceAsync(invoiceId: 1, total: 500m);
        await SeedPendingPaymentAsync(paymentId: 1, invoiceId: 1, amount: 200m, trackId: "TRK_PARTIAL_A");
        await SeedPendingPaymentAsync(paymentId: 2, invoiceId: 1, amount: 300m, trackId: "TRK_PARTIAL_B");

        // First half captures.
        await _sut.HandleKnetResponseAsync(null, "KP_PA", "TRK_PARTIAL_A", "TX_A", "CAPTURED", null);
        var afterFirst = await _ctx.Invoices.AsNoTracking().FirstAsync();
        afterFirst.Status.Should().Be("Partially Paid");

        // Second half captures — must flip to Paid (not back to Pending or
        // any other state).
        await _sut.HandleKnetResponseAsync(null, "KP_PB", "TRK_PARTIAL_B", "TX_B", "CAPTURED", null);

        var afterSecond = await _ctx.Invoices.AsNoTracking().FirstAsync();
        afterSecond.Status.Should().Be("Paid");
        afterSecond.PaidAmount.Should().Be(500m);
    }

    [Fact]
    public async Task Completed_payment_status_does_not_revert_on_replay()
    {
        await SeedInvoiceAsync(invoiceId: 1, total: 500m);
        await SeedPendingPaymentAsync(paymentId: 1, invoiceId: 1, amount: 500m, trackId: "TRK_NOREVERT");

        await _sut.HandleKnetResponseAsync(null, "KP_N", "TRK_NOREVERT", "TX_N", "CAPTURED", null);
        var first = await _ctx.Payments.AsNoTracking().FirstAsync();
        first.Status.Should().Be(PaymentStatus.Completed);
        var firstReceipt = first.ReceiptNumber;

        // K-NET retries the callback with FAILED — must NOT downgrade us.
        await _sut.HandleKnetResponseAsync(null, "KP_N", "TRK_NOREVERT", "TX_N", "FAILED", null);

        var second = await _ctx.Payments.AsNoTracking().FirstAsync();
        second.Status.Should().Be(PaymentStatus.Completed,
            "a Completed payment must NEVER revert to Pending/Failed via callback");
        second.ReceiptNumber.Should().Be(firstReceipt,
            "the receipt number is stable once issued — re-callbacks must not re-stamp it");
    }

    [Fact]
    public async Task Receipt_number_is_only_stamped_on_CAPTURED_not_on_failure()
    {
        await SeedInvoiceAsync(invoiceId: 1, total: 500m);
        await SeedPendingPaymentAsync(paymentId: 1, invoiceId: 1, amount: 500m, trackId: "TRK_FAIL_NORCPT");

        await _sut.HandleKnetResponseAsync(null, "KP_F", "TRK_FAIL_NORCPT", "TX_F", "CANCELED", null);

        var payment = await _ctx.Payments.AsNoTracking().FirstAsync();
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.ReceiptNumber.Should().BeNullOrEmpty(
            "a Failed payment is not a transaction — no receipt should be issued");
    }

    [Fact]
    public async Task Overpayment_is_rejected_by_amount_mismatch_guard()
    {
        // Invoice = 500 KWD, our pending Payment row = 500 KWD. Callback
        // claims 5000 KWD was captured — somebody (gateway bug, attacker)
        // is trying to over-credit us. Amount-mismatch must catch it.
        await SeedInvoiceAsync(invoiceId: 1, total: 500m);
        await SeedPendingPaymentAsync(paymentId: 1, invoiceId: 1, amount: 500m, trackId: "TRK_OVER");

        var clear = "result=CAPTURED&paymentid=KP_OVER&trackid=TRK_OVER&tranid=TX_O&amt=5000.000";
        var trandata = new KnetUtil().EncryptUrlParams(clear, "0123456789ABCDEF");

        var result = await _sut.HandleKnetResponseAsync(
            trandata, "KP_OVER", "TRK_OVER", "TX_O", "CAPTURED", null);

        result.Success.Should().BeFalse();
        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync();
        invoice.PaidAmount.Should().Be(0m,
            "over-amount callback must NOT credit the invoice even with a positive delta");
        invoice.Status.Should().NotBe("Paid");
    }

    // ───────────── helpers (shared with HandleKnetResponseTests) ─────────────

    private async Task SeedInvoiceAsync(int invoiceId, decimal total)
    {
        if (!_ctx.Companies.Local.Any() && !_ctx.Companies.Any())
        {
            _ctx.Companies.Add(new Company { CompanyId = 1, CompanyName = "T", Email = "t@t.com", Phone = "1", Address = "x", IsActive = true });
            _ctx.Tenants.Add(new Tenant
            {
                TenantId = 1, CompanyId = 1,
                FirstName = "T", LastName = "X", Email = "t@x.com", Phone = "1",
                GovernmentId = "GID1", Status = "Active",
            });
            _ctx.Properties.Add(new Property
            {
                PropertyId = 1, CompanyId = 1, PropertyName = "P",
                PropertyType = "Residential", Address = "x", Status = "Active",
                AcceptKNET = true, KnetTerminalResourceKey = "0123456789ABCDEF",
            });
            _ctx.Units.Add(new Unit { UnitId = 1, PropertyId = 1, UnitNumber = "U1", UnitType = "Apartment", Status = "Occupied" });
            _ctx.Leases.Add(new Lease
            {
                LeaseId = 1, UnitId = 1, TenantId = 1,
                LeaseStartDate = DateTime.UtcNow.AddMonths(-3),
                LeaseEndDate = DateTime.UtcNow.AddMonths(9),
                MonthlyRent = total, Status = "Active",
            });
        }
        _ctx.Invoices.Add(new Invoice
        {
            InvoiceId = invoiceId, LeaseId = 1, TenantId = 1,
            InvoiceDate = DateTime.UtcNow, DueDate = DateTime.UtcNow.AddDays(7),
            TotalAmount = total, PaidAmount = 0m,
            Status = "Pending", InvoiceType = "Rent",
        });
        await _ctx.SaveChangesAsync();
    }

    private async Task SeedPendingPaymentAsync(int paymentId, int invoiceId, decimal amount, string trackId)
    {
        _ctx.Payments.Add(new Payment
        {
            PaymentId = paymentId, LeaseId = 1, TenantId = 1,
            InvoiceId = invoiceId, PropertyId = 1,
            PaymentDate = DateTime.UtcNow, AmountPaid = amount,
            PaymentType = PaymentType.KNET, Status = PaymentStatus.Pending,
            TransactionReference = trackId, CivilID = "GID1",
        });
        await _ctx.SaveChangesAsync();
    }

    private sealed class TestRepo<T> : IRepository<T> where T : class
    {
        private readonly ApplicationDbContext _ctx;
        public TestRepo(ApplicationDbContext ctx) => _ctx = ctx;
        public async Task<T?> GetByIdAsync(int id) => await _ctx.Set<T>().FindAsync(id);
        public async Task<IEnumerable<T>> GetAllAsync() => await _ctx.Set<T>().ToListAsync();
        public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>> p) => await _ctx.Set<T>().Where(p).ToListAsync();
        public async Task<T?> GetFirstOrDefaultAsync(Expression<Func<T, bool>> p) => await _ctx.Set<T>().FirstOrDefaultAsync(p);
        public async Task<T> AddAsync(T entity) { _ctx.Set<T>().Add(entity); await _ctx.SaveChangesAsync(); return entity; }
        public async Task UpdateAsync(T entity) { _ctx.Set<T>().Update(entity); await _ctx.SaveChangesAsync(); }
        public async Task DeleteAsync(T entity) { _ctx.Set<T>().Remove(entity); await _ctx.SaveChangesAsync(); }
        public async Task<int> CountAsync(Expression<Func<T, bool>>? p = null)
            => p == null ? await _ctx.Set<T>().CountAsync() : await _ctx.Set<T>().CountAsync(p);
        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> p) => await _ctx.Set<T>().AnyAsync(p);
    }
}
