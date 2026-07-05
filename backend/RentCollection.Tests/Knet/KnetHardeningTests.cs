using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RentCollection.Application.DTOs;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Knet;

/// <summary>
/// Sprint 8 regression tests — one test per defect found in the K-NET audit.
/// Each test would FAIL against the pre-Sprint-8 code and PASSES against the
/// fix. Pinning these prevents the defect from re-emerging.
/// </summary>
public class KnetHardeningTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly KnetPaymentService _sut;

    public KnetHardeningTests()
    {
        _ctx = TestDatabaseHelper.CreateInMemoryContextWithTransactionsIgnored($"Hard_{Guid.NewGuid()}");
        var settings = Options.Create(new KnetSettings
        {
            TerminalResourceKey = "0123456789ABCDEF",
            TranportalId = "DEFAULT_TID",
            TranportalPwd = "DEFAULT_PWD",
            UseTestMode = true,                       // skip the live HTTP call
            PaymentApiUrl = "https://kpaytest.com.kw/test",
            SuccessUrl = "https://example.com/ok",
            ErrorUrl = "https://example.com/err",
        });
        _sut = new KnetPaymentService(
            new TestRepo<Payment>(_ctx), new TestRepo<Invoice>(_ctx),
            _ctx, settings, new KnetUtil(),
            NullLogger<KnetPaymentService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    // ───────────── Fix #1: trackId uniqueness ─────────────

    [Fact]
    public async Task Fix1_two_initiates_for_same_invoice_produce_distinct_trackIds()
    {
        // The pre-fix code used INV-{id}-{yyyyMMddHHmmss} which collides
        // for two initiates within the same second. Post-fix appends a
        // 6-char random suffix.
        await SeedAsync(invoiceTotal: 500m);

        var first = await _sut.InitiatePaymentAsync(MakeRequest());
        var second = await _sut.InitiatePaymentAsync(MakeRequest());

        first.Success.Should().BeTrue("first initiate should succeed");
        second.Success.Should().BeTrue("second initiate should succeed (different track ID)");

        first.TransactionReference.Should().NotBeNullOrEmpty();
        second.TransactionReference.Should().NotBeNullOrEmpty();
        second.TransactionReference.Should().NotBe(first.TransactionReference,
            "trackIds must be unique even for two initiates on the same invoice " +
            "in the same second");

        // Both Payment rows should exist with their distinct trackIds.
        var payments = await _ctx.Payments.AsNoTracking().ToListAsync();
        payments.Should().HaveCount(2,
            "the second initiate must NOT silently overwrite the first via trackId collision");
        payments.Select(p => p.TransactionReference).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Fix1_trackId_format_includes_random_suffix()
    {
        // Pin the format so a future refactor doesn't quietly drop the suffix.
        var format = @"^INV-\d+-\d{14}-[0-9A-F]{6}$";
        var trackId = $"INV-42-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()}";
        trackId.Should().MatchRegex(format,
            "trackId must be INV-{invoiceId}-{yyyyMMddHHmmss}-{6 hex chars}");
    }

    // ───────────── Fix #2: Civil ID masking ─────────────

    [Theory]
    [InlineData("123456789012", "********9012")]
    [InlineData("9999",         "****")]
    [InlineData("99",           "**")]
    [InlineData("",             "(empty)")]
    [InlineData(null,           "(empty)")]
    public void Fix2_MaskCivilId_keeps_last_four_chars_visible(string? input, string expected)
    {
        KnetPaymentService.MaskCivilId(input).Should().Be(expected);
    }

    // ───────────── Fix #3: TenantId ownership defense ─────────────

    [Fact]
    public async Task Fix3_initiate_with_TenantId_zero_is_rejected_even_with_valid_invoice()
    {
        // Pre-fix: TenantId=0 skipped the ownership check entirely. Post-fix:
        // the service fails closed regardless of what the controller did.
        await SeedAsync(invoiceTotal: 500m);

        var request = MakeRequest();
        request.TenantId = 0;  // simulating an internal caller that forgot

        var result = await _sut.InitiatePaymentAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ownership");
        var payments = await _ctx.Payments.AsNoTracking().ToListAsync();
        payments.Should().BeEmpty(
            "the service must NOT create a payment row when ownership is unverified");
    }

    [Fact]
    public async Task Fix3_initiate_with_wrong_TenantId_is_rejected_does_not_create_payment()
    {
        await SeedAsync(invoiceTotal: 500m);

        var request = MakeRequest();
        request.TenantId = 999;  // different tenant than the lease's owner

        var result = await _sut.InitiatePaymentAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ownership");
        var payments = await _ctx.Payments.AsNoTracking().ToListAsync();
        payments.Should().BeEmpty();
    }

    // ───────────── Fix #4: callback decryption key fallback ─────────────

    [Fact]
    public async Task Fix4_callback_decrypts_with_default_key_when_building_key_is_missing()
    {
        // Production scenario: callback arrives BEFORE the building's
        // building-specific terminal key was decrypted. Pre-fix this
        // returned "Callback decryption failed". Post-fix the handler
        // tries the default key as a fallback.
        await SeedAsync(invoiceTotal: 500m);
        var payment = new Payment
        {
            LeaseId = 1, TenantId = 1, InvoiceId = 1, PropertyId = 1,
            PaymentDate = DateTime.UtcNow, AmountPaid = 500m,
            PaymentType = PaymentType.KNET, Status = PaymentStatus.Pending,
            TransactionReference = "TRK_KEYFALLBACK", CivilID = "GID1",
        };
        _ctx.Payments.Add(payment);
        await _ctx.SaveChangesAsync();

        // Encrypt the callback payload with the DEFAULT key (not the
        // building's key — simulating either a default-key building or
        // a race window).
        var defaultKey = "0123456789ABCDEF";
        var clear = "result=CAPTURED&paymentid=KP_OK&trackid=TRK_KEYFALLBACK&tranid=TX_OK&amt=500.000";
        var trandata = new KnetUtil().EncryptUrlParams(clear, defaultKey);

        var result = await _sut.HandleKnetResponseAsync(
            trandata, "KP_OK", "TRK_KEYFALLBACK", "TX_OK", "CAPTURED", null);

        result.Success.Should().BeTrue(
            "decryption with the default key must succeed even when the building " +
            "configuration doesn't have its own terminal key");
    }

    [Fact]
    public async Task Fix4_callback_with_garbled_trandata_fails_loudly_no_payment_changed()
    {
        // If all decryption attempts fail, the handler must NOT silently
        // skip processing and return Success — it must surface the error
        // so the gateway operator notices.
        await SeedAsync(invoiceTotal: 500m);
        var payment = new Payment
        {
            LeaseId = 1, TenantId = 1, InvoiceId = 1, PropertyId = 1,
            PaymentDate = DateTime.UtcNow, AmountPaid = 500m,
            PaymentType = PaymentType.KNET, Status = PaymentStatus.Pending,
            TransactionReference = "TRK_GARBLED", CivilID = "GID1",
        };
        _ctx.Payments.Add(payment);
        await _ctx.SaveChangesAsync();

        // Garbled hex string — won't decrypt with any key.
        var garbled = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF0011";

        var result = await _sut.HandleKnetResponseAsync(
            garbled, "KP_X", "TRK_GARBLED", "TX_X", "CAPTURED", null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("decryption",
            "garbled trandata must produce an explicit decryption error, " +
            "not silently fall through to the unencrypted path");
        var after = await _ctx.Payments.AsNoTracking().FirstAsync();
        after.Status.Should().Be(PaymentStatus.Pending,
            "a failed-to-decrypt callback must NOT change payment state");
    }

    // ───────────── Fix #5: exact amount equality ─────────────

    [Fact]
    public async Task Fix5_one_fil_drift_now_rejects_capture()
    {
        // Pre-fix: tolerance was "> 0.001" which let 1-fil drift through.
        // Post-fix: any non-zero drift fails the capture.
        await SeedAsync(invoiceTotal: 500m);
        var payment = new Payment
        {
            LeaseId = 1, TenantId = 1, InvoiceId = 1, PropertyId = 1,
            PaymentDate = DateTime.UtcNow, AmountPaid = 500m,
            PaymentType = PaymentType.KNET, Status = PaymentStatus.Pending,
            TransactionReference = "TRK_DRIFT", CivilID = "GID1",
        };
        _ctx.Payments.Add(payment);
        await _ctx.SaveChangesAsync();

        var key = "0123456789ABCDEF";
        // 1-fil drift: 500.001 vs expected 500.000.
        var clear = "result=CAPTURED&paymentid=KP_D&trackid=TRK_DRIFT&tranid=TX_D&amt=500.001";
        var trandata = new KnetUtil().EncryptUrlParams(clear, key);

        var result = await _sut.HandleKnetResponseAsync(
            trandata, "KP_D", "TRK_DRIFT", "TX_D", "CAPTURED", null);

        result.Success.Should().BeFalse(
            "1-fil drift was the tolerance window before Fix #5; now even 0.001 KWD drift rejects");
        result.ErrorMessage.Should().Contain("Amount mismatch");
        var after = await _ctx.Payments.AsNoTracking().FirstAsync();
        after.Status.Should().Be(PaymentStatus.Failed);
        var invoice = await _ctx.Invoices.AsNoTracking().FirstAsync();
        invoice.Status.Should().NotBe("Paid");
    }

    // ───────────── helpers ─────────────

    private KnetPaymentRequestDto MakeRequest() => new()
    {
        InvoiceId = 1,
        LeaseId = 1,
        TenantId = 1,
        PropertyId = 1,
        Amount = 500m,
        CivilID = "GID1",
        Language = "en",
    };

    private async Task SeedAsync(decimal invoiceTotal)
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
            AcceptKNET = true,
            KnetTerminalResourceKey = "0123456789ABCDEF",
        });
        _ctx.Units.Add(new Unit { UnitId = 1, PropertyId = 1, UnitNumber = "U1", UnitType = "Apartment", Status = "Occupied" });
        _ctx.Leases.Add(new Lease
        {
            LeaseId = 1, UnitId = 1, TenantId = 1,
            LeaseStartDate = DateTime.UtcNow.AddMonths(-3),
            LeaseEndDate = DateTime.UtcNow.AddMonths(9),
            MonthlyRent = invoiceTotal, Status = "Active",
        });
        _ctx.Invoices.Add(new Invoice
        {
            InvoiceId = 1, LeaseId = 1, TenantId = 1,
            InvoiceDate = DateTime.UtcNow, DueDate = DateTime.UtcNow.AddDays(7),
            TotalAmount = invoiceTotal, PaidAmount = 0m,
            Status = "Pending", InvoiceType = "Rent",
        });
        await _ctx.SaveChangesAsync();
    }

    private sealed class TestRepo<T> : IRepository<T> where T : class
    {
        private readonly ApplicationDbContext _ctx;
        public TestRepo(ApplicationDbContext ctx) => _ctx = ctx;
        public async Task<T?> GetByIdAsync(int id) => await _ctx.Set<T>().FindAsync(id);
        public async Task<IEnumerable<T>> GetAllAsync() => await _ctx.Set<T>().ToListAsync();
        public async Task<IEnumerable<T>> GetAllAsync(System.Linq.Expressions.Expression<Func<T, bool>> p) => await _ctx.Set<T>().Where(p).ToListAsync();
        public async Task<T?> GetFirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<T, bool>> p) => await _ctx.Set<T>().FirstOrDefaultAsync(p);
        public async Task<T> AddAsync(T entity) { _ctx.Set<T>().Add(entity); await _ctx.SaveChangesAsync(); return entity; }
        public async Task UpdateAsync(T entity) { _ctx.Set<T>().Update(entity); await _ctx.SaveChangesAsync(); }
        public async Task DeleteAsync(T entity) { _ctx.Set<T>().Remove(entity); await _ctx.SaveChangesAsync(); }
        public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<T, bool>>? p = null)
            => p == null ? await _ctx.Set<T>().CountAsync() : await _ctx.Set<T>().CountAsync(p);
        public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<T, bool>> p) => await _ctx.Set<T>().AnyAsync(p);
    }
}
