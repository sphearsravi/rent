using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Knet;

/// <summary>
/// Sprint 7 Tier 1 — coverage for <see cref="KnetPaymentService.HandleKnetResponseAsync"/>.
/// This is the single most security-critical method in the codebase: it
/// turns an HTTP callback from K-NET into a Payment row, marks the invoice
/// paid, and trusts the caller about the amount. A bug here is the
/// difference between a tenant paying their rent and someone tampering
/// with the books.
///
/// Every callback here carries a real AES-encrypted <c>trandata</c> blob built
/// by <see cref="Enc"/>, because the handler (audit C1) rejects any callback
/// WITHOUT trandata as unauthenticated — a plain <c>result=CAPTURED</c> query
/// string is forgeable and must never credit an invoice. The cleartext params
/// are still passed too, mirroring what K-NET posts; the decrypted values win.
/// </summary>
public class HandleKnetResponseTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly KnetPaymentService _sut;

    public HandleKnetResponseTests()
    {
        _ctx = TestDatabaseHelper.CreateInMemoryContextWithTransactionsIgnored($"Knet_{Guid.NewGuid()}");

        // Repositories sit over the same DbContext so the in-memory store
        // is the single source of truth — matches production wiring.
        var paymentRepo = new TestRepo<Payment>(_ctx);
        var invoiceRepo = new TestRepo<Invoice>(_ctx);
        var settings = Options.Create(new KnetSettings { TerminalResourceKey = "0123456789ABCDEF" });

        _sut = new KnetPaymentService(
            paymentRepository: paymentRepo,
            invoiceRepository: invoiceRepo,
            context: _ctx,
            settings: settings,
            knetUtil: new KnetUtil(),
            logger: NullLogger<KnetPaymentService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    // ───────────── Happy path ─────────────

    [Fact]
    public async Task Captured_callback_marks_invoice_paid_and_creates_completion_state()
    {
        await SeedAsync(invoiceAmount: 500m, paymentAmount: 500m, trackId: "TRK_HAPPY");

        var result = await _sut.HandleKnetResponseAsync(
            trandata: Enc("CAPTURED", "KP_001", "TRK_HAPPY", "TX_001", 500m),
            paymentId: "KP_001", trackId: "TRK_HAPPY",
            tranId: "TX_001", result: "CAPTURED", error: null);

        result.Success.Should().BeTrue();
        result.Status.Should().Be(PaymentStatus.Completed.ToString());
        var payment = await _ctx.Payments.AsNoTracking().FirstAsync();
        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.CheckoutPaymentId.Should().Be("KP_001");
        payment.ReceiptNumber.Should().StartWith("RCPT-");
        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync();
        invoice.Status.Should().Be("Paid");
        invoice.PaidAmount.Should().Be(500m);
    }

    // ───────────── Idempotency ─────────────

    [Fact]
    public async Task Same_callback_received_twice_does_not_double_credit_invoice()
    {
        await SeedAsync(invoiceAmount: 500m, paymentAmount: 500m, trackId: "TRK_IDEMP");

        // First callback — succeeds.
        var first = await _sut.HandleKnetResponseAsync(Enc("CAPTURED", "KP_002", "TRK_IDEMP", "TX_002", 500m), "KP_002", "TRK_IDEMP", "TX_002", "CAPTURED", null);
        first.Success.Should().BeTrue();

        // Second callback — must be a no-op, NOT a second invoice credit.
        var second = await _sut.HandleKnetResponseAsync(Enc("CAPTURED", "KP_002", "TRK_IDEMP", "TX_002", 500m), "KP_002", "TRK_IDEMP", "TX_002", "CAPTURED", null);

        second.Success.Should().BeTrue("re-callback of an already-completed payment is still the right outcome");
        var payments = await _ctx.Payments.AsNoTracking().ToListAsync();
        payments.Should().HaveCount(1, "the callback handler must NEVER insert a second Payment row");
        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync();
        invoice.PaidAmount.Should().Be(500m, "double-callback must not double-credit the invoice");
    }

    [Fact]
    public async Task Failed_callback_then_re_callback_does_not_flip_to_Completed()
    {
        await SeedAsync(invoiceAmount: 500m, paymentAmount: 500m, trackId: "TRK_FAIL");

        // First callback — failed.
        await _sut.HandleKnetResponseAsync(Enc("CANCELED", "KP_003", "TRK_FAIL", "TX_003", 500m), "KP_003", "TRK_FAIL", "TX_003", "CANCELED", null);
        var afterFail = await _ctx.Payments.AsNoTracking().FirstAsync();
        afterFail.Status.Should().Be(PaymentStatus.Failed);

        // Replay arrives with CAPTURED — must NOT promote a Failed payment to Completed.
        // The idempotency guard checks `payment.Status != Pending` and returns early.
        await _sut.HandleKnetResponseAsync(Enc("CAPTURED", "KP_003", "TRK_FAIL", "TX_003", 500m), "KP_003", "TRK_FAIL", "TX_003", "CAPTURED", null);

        var afterReplay = await _ctx.Payments.AsNoTracking().FirstAsync();
        afterReplay.Status.Should().Be(PaymentStatus.Failed,
            "a Failed payment must NOT be flipped to Completed by a late CAPTURED callback");
        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync();
        invoice.PaidAmount.Should().Be(0m, "invoice must not be credited from a Failed→Captured flip");
    }

    // ───────────── Amount tampering ─────────────

    [Fact]
    public async Task Callback_with_wrong_amount_records_failure_and_does_not_credit_invoice()
    {
        // The invoice and our Payment row both record 500 KWD. K-NET callback
        // claims it processed 0.001 KWD — classic amount-tampering replay.
        await SeedAsync(invoiceAmount: 500m, paymentAmount: 500m, trackId: "TRK_TAMPER");

        // Build the AES-encrypted trandata the production handler expects.
        var key = "0123456789ABCDEF";
        var clear = "result=CAPTURED&paymentid=KP_TAMP&trackid=TRK_TAMPER&tranid=TX_T&amt=0.001";
        var trandata = new KnetUtil().EncryptUrlParams(clear, key);

        var result = await _sut.HandleKnetResponseAsync(
            trandata, "KP_TAMP", "TRK_TAMPER", "TX_T", "CAPTURED", null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Amount mismatch");
        var payment = await _ctx.Payments.AsNoTracking().FirstAsync();
        payment.Status.Should().Be(PaymentStatus.Failed,
            "amount-tampered CAPTURED must NOT credit the invoice — even if the gateway insists");
        payment.Notes.Should().Contain("Amount mismatch");
        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync();
        invoice.PaidAmount.Should().Be(0m);
        invoice.Status.Should().NotBe("Paid");
    }

    // ───────────── Non-CAPTURED results ─────────────

    [Theory]
    [InlineData("CANCELED")]
    [InlineData("DENIED")]
    [InlineData("HOST TIMEOUT")]
    [InlineData("not-a-real-result-code")]
    public async Task Non_captured_result_marks_payment_failed_does_not_touch_invoice(string knetResult)
    {
        var trackId = $"TRK_{knetResult.Replace(" ", "_")}";
        await SeedAsync(invoiceAmount: 500m, paymentAmount: 500m, trackId: trackId);

        var result = await _sut.HandleKnetResponseAsync(
            Enc(knetResult, "KP_NC", trackId, "TX_NC", 500m, error: "0001", errorText: "test"),
            "KP_NC", trackId, "TX_NC", knetResult, error: "0001", errorText: "test");

        result.Success.Should().BeFalse();
        var payment = await _ctx.Payments.AsNoTracking().FirstAsync(p => p.TransactionReference == trackId);
        payment.Status.Should().Be(PaymentStatus.Failed,
            $"K-NET result '{knetResult}' must produce a Failed payment, never Completed");
        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync(i => i.InvoiceId == payment.InvoiceId);
        invoice.Status.Should().NotBe("Paid",
            "a non-CAPTURED callback must NEVER mark the invoice paid");
        invoice.PaidAmount.Should().Be(0m);
    }

    // ───────────── Unknown trackid ─────────────

    [Fact]
    public async Task Callback_with_unknown_trackid_returns_error_does_not_create_payment_row()
    {
        // No payment seeded — but a callback arrives. Could be a stale
        // sandbox test, could be someone POSTing a forged payload.
        var result = await _sut.HandleKnetResponseAsync(
            Enc("CAPTURED", "KP_GHOST", "TRK_NOT_EXIST", "TX_G", 500m),
            "KP_GHOST", "TRK_NOT_EXIST", "TX_G", "CAPTURED", null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Payment record not found");
        var count = await _ctx.Payments.AsNoTracking().CountAsync();
        count.Should().Be(0, "the handler must NEVER fabricate a Payment row from a callback");
    }

    [Fact]
    public async Task Callback_with_missing_trackid_returns_error()
    {
        // Encrypted blob deliberately carries NO trackid, and none is passed in
        // the clear either — the handler must say so rather than guess.
        var result = await _sut.HandleKnetResponseAsync(
            Enc("CAPTURED", "KP_X", trackId: null, "TX_X", 500m),
            "KP_X", trackId: null, "TX_X", "CAPTURED", null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Missing track ID");
    }

    // ───────────── helpers ─────────────

    private const string TestKey = "0123456789ABCDEF";

    /// <summary>
    /// Build the AES-encrypted <c>trandata</c> blob K-NET posts, using the same
    /// key the test settings/property are seeded with. <paramref name="trackId"/>
    /// null omits the field entirely (for the missing-trackid case). Keys match
    /// what <see cref="KnetUtil.ParseResponseParams"/> is read with in the handler
    /// (lowercase result/paymentid/trackid/tranid/amt; "Error"/"ErrorText").
    /// </summary>
    private static string Enc(
        string result, string paymentId, string? trackId, string tranId, decimal amt,
        string? error = null, string? errorText = null)
    {
        var parts = new List<string> { $"result={result}", $"paymentid={paymentId}" };
        if (trackId != null) parts.Add($"trackid={trackId}");
        parts.Add($"tranid={tranId}");
        parts.Add($"amt={amt.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
        if (error != null) parts.Add($"Error={error}");
        if (errorText != null) parts.Add($"ErrorText={errorText}");
        return new KnetUtil().EncryptUrlParams(string.Join("&", parts), TestKey);
    }

    /// <summary>
    /// Seed: 1 company, 1 tenant, 1 property, 1 unit, 1 lease, 1 invoice,
    /// 1 Pending Payment with the given trackId. Designed for the callback
    /// handler to pick up the Payment via TransactionReference.
    /// </summary>
    private async Task SeedAsync(decimal invoiceAmount, decimal paymentAmount, string trackId)
    {
        _ctx.Companies.Add(new Company { CompanyId = 1, CompanyName = "T", Email = "t@t.com", Phone = "1", Address = "x", IsActive = true });
        _ctx.Tenants.Add(new Tenant
        {
            TenantId = 1, CompanyId = 1,
            FirstName = "T", LastName = "X",
            Email = "t@x.com", Phone = "1", GovernmentId = "GID1", Status = "Active",
        });
        _ctx.Properties.Add(new Property
        {
            PropertyId = 1, CompanyId = 1, PropertyName = "P",
            PropertyType = "Residential", Address = "x", Status = "Active",
            AcceptKNET = true,
            KnetTerminalResourceKey = "0123456789ABCDEF",
        });
        _ctx.Units.Add(new Unit
        {
            UnitId = 1, PropertyId = 1,
            UnitNumber = "U1", UnitType = "Apartment", Status = "Occupied",
        });
        _ctx.Leases.Add(new Lease
        {
            LeaseId = 1, UnitId = 1, TenantId = 1,
            LeaseStartDate = DateTime.UtcNow.AddMonths(-3),
            LeaseEndDate = DateTime.UtcNow.AddMonths(9),
            MonthlyRent = invoiceAmount, Status = "Active",
        });
        _ctx.Invoices.Add(new Invoice
        {
            InvoiceId = 1, LeaseId = 1, TenantId = 1,
            InvoiceDate = DateTime.UtcNow, DueDate = DateTime.UtcNow.AddDays(7),
            TotalAmount = invoiceAmount, PaidAmount = 0m,
            Status = "Pending", InvoiceType = "Rent",
        });
        _ctx.Payments.Add(new Payment
        {
            LeaseId = 1, TenantId = 1, InvoiceId = 1, PropertyId = 1,
            PaymentDate = DateTime.UtcNow,
            AmountPaid = paymentAmount,
            PaymentType = PaymentType.KNET,
            Status = PaymentStatus.Pending,
            TransactionReference = trackId,
            CivilID = "GID1",
        });
        await _ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Thin in-memory IRepository{T} for the service. The production
    /// implementation wraps DbContext; we wrap the same DbContext so a
    /// single AddRange+SaveChanges populates both.
    /// </summary>
    private sealed class TestRepo<T> : IRepository<T> where T : class
    {
        private readonly ApplicationDbContext _ctx;
        public TestRepo(ApplicationDbContext ctx) => _ctx = ctx;
        public async Task<T?> GetByIdAsync(int id) => await _ctx.Set<T>().FindAsync(id);
        public async Task<IEnumerable<T>> GetAllAsync() => await _ctx.Set<T>().ToListAsync();
        public async Task<IEnumerable<T>> GetAllAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
            => await _ctx.Set<T>().Where(predicate).ToListAsync();
        public async Task<T?> GetFirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
            => await _ctx.Set<T>().FirstOrDefaultAsync(predicate);
        public async Task<T> AddAsync(T entity) { _ctx.Set<T>().Add(entity); await _ctx.SaveChangesAsync(); return entity; }
        public async Task UpdateAsync(T entity) { _ctx.Set<T>().Update(entity); await _ctx.SaveChangesAsync(); }
        public async Task DeleteAsync(T entity) { _ctx.Set<T>().Remove(entity); await _ctx.SaveChangesAsync(); }
        public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<T, bool>>? predicate = null)
            => predicate == null ? await _ctx.Set<T>().CountAsync() : await _ctx.Set<T>().CountAsync(predicate);
        public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate)
            => await _ctx.Set<T>().AnyAsync(predicate);
    }
}
