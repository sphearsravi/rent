using FluentAssertions;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Services;

/// <summary>
/// Sprint 24 — unit coverage for the "oldest unpaid first" rule.
/// Each test seeds a tiny invoice set against a fresh in-memory DB
/// then asks the service whether a given invoice is payable.
///
/// Ordering is by InvoiceDate then InvoiceId — never DueDate. This
/// matters when a late-issued invoice for an earlier period should
/// still queue ahead of an on-time newer one.
/// </summary>
public class PaymentEligibilityServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PaymentEligibilityService _sut;
    private const int TenantA = 100;
    private const int TenantB = 200;

    public PaymentEligibilityServiceTests()
    {
        _db = TestDatabaseHelper.CreateInMemoryContext(Guid.NewGuid().ToString());
        _sut = new PaymentEligibilityService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task NoInvoices_InvoiceNotFound()
    {
        var result = await _sut.CheckAsync(999);

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("not found");
    }

    [Fact]
    public async Task SingleUnpaidInvoice_IsEligible()
    {
        var inv = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(inv.InvoiceId);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task ThreeUnpaid_PayingNewest_Blocked()
    {
        var jan = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 0m, InvoiceStatuses.Pending);
        var feb = await Seed(TenantA, new DateTime(2026, 2, 1), 300m, 0m, InvoiceStatuses.Pending);
        var mar = await Seed(TenantA, new DateTime(2026, 3, 1), 300m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(mar.InvoiceId);

        result.IsEligible.Should().BeFalse();
        result.OldestUnpaidInvoiceId.Should().Be(jan.InvoiceId);
        result.OldestBalance.Should().Be(300m);
    }

    [Fact]
    public async Task ThreeUnpaid_PayingOldest_IsEligible()
    {
        var jan = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 0m, InvoiceStatuses.Pending);
        await Seed(TenantA, new DateTime(2026, 2, 1), 300m, 0m, InvoiceStatuses.Pending);
        await Seed(TenantA, new DateTime(2026, 3, 1), 300m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(jan.InvoiceId);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task OldestPartiallyPaid_NewerStillBlocked()
    {
        // Jan is "Partially Paid" with 100 of 300 collected. Feb should
        // still be blocked — partial doesn't unlock the queue.
        var jan = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 100m, InvoiceStatuses.PartiallyPaid);
        var feb = await Seed(TenantA, new DateTime(2026, 2, 1), 300m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(feb.InvoiceId);

        result.IsEligible.Should().BeFalse();
        result.OldestUnpaidInvoiceId.Should().Be(jan.InvoiceId);
        result.OldestBalance.Should().Be(200m); // 300 total - 100 paid
    }

    [Fact]
    public async Task OldestPartiallyPaid_PayingItRemainsEligible()
    {
        var jan = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 100m, InvoiceStatuses.PartiallyPaid);
        await Seed(TenantA, new DateTime(2026, 2, 1), 300m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(jan.InvoiceId);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task AllInvoicesPaid_AnyQueryIsEligible()
    {
        var jan = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 300m, InvoiceStatuses.Paid);
        var feb = await Seed(TenantA, new DateTime(2026, 2, 1), 300m, 300m, InvoiceStatuses.Paid);

        // Even querying a paid invoice should be eligible — there's
        // simply nothing in the queue to block against.
        (await _sut.CheckAsync(jan.InvoiceId)).IsEligible.Should().BeTrue();
        (await _sut.CheckAsync(feb.InvoiceId)).IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task MixedTypes_DepositRentLateFee_QueuedByInvoiceDate()
    {
        // Deposit issued Jan 1 (oldest), rent Jan 5, late fee Feb 2.
        // Even though late fees are conceptually "extra", they queue by
        // their own issue date — same rule, no special-casing.
        var deposit = await Seed(TenantA, new DateTime(2026, 1, 1), 500m, 0m, InvoiceStatuses.Pending, "Deposit");
        var rent = await Seed(TenantA, new DateTime(2026, 1, 5), 300m, 0m, InvoiceStatuses.Pending, "Rent");
        var lateFee = await Seed(TenantA, new DateTime(2026, 2, 2), 25m, 0m, InvoiceStatuses.Pending, "LateFee");

        (await _sut.CheckAsync(lateFee.InvoiceId)).OldestUnpaidInvoiceId.Should().Be(deposit.InvoiceId);
        (await _sut.CheckAsync(rent.InvoiceId)).OldestUnpaidInvoiceId.Should().Be(deposit.InvoiceId);
        (await _sut.CheckAsync(deposit.InvoiceId)).IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task SameDateInvoices_OrderedByInvoiceId()
    {
        // Two invoices issued the same nightly batch — InvoiceId is the
        // deterministic tie-breaker so the result doesn't drift.
        var rent = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 0m, InvoiceStatuses.Pending, "Rent");
        var insurance = await Seed(TenantA, new DateTime(2026, 1, 1), 10m, 0m, InvoiceStatuses.Pending, "Insurance");

        var result = await _sut.CheckAsync(insurance.InvoiceId);

        result.IsEligible.Should().BeFalse();
        // The first-inserted (rent) gets the lower InvoiceId and wins.
        result.OldestUnpaidInvoiceId.Should().Be(rent.InvoiceId);
    }

    [Fact]
    public async Task CrossTenant_OtherTenantsInvoicesDoNotBlock()
    {
        // Tenant A has an old unpaid invoice. Tenant B should still be
        // able to pay their own — the queue is per-tenant.
        await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 0m, InvoiceStatuses.Pending);
        var bsInvoice = await Seed(TenantB, new DateTime(2026, 3, 1), 400m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(bsInvoice.InvoiceId);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task CancelledInvoice_DoesNotBlockNewer()
    {
        // A cancelled invoice (e.g. wrong amount, voided) shouldn't
        // hold up the queue.
        await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 0m, InvoiceStatuses.Cancelled);
        var feb = await Seed(TenantA, new DateTime(2026, 2, 1), 300m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(feb.InvoiceId);

        result.IsEligible.Should().BeTrue();
    }

    [Fact]
    public async Task OverdueStatus_QueuesLikePending()
    {
        // Overdue is a display flag; for queue purposes it's identical to
        // Pending. The oldest Overdue must still be paid before any newer.
        var overdueJan = await Seed(TenantA, new DateTime(2026, 1, 1), 300m, 0m, InvoiceStatuses.Overdue);
        var feb = await Seed(TenantA, new DateTime(2026, 2, 1), 300m, 0m, InvoiceStatuses.Pending);

        var result = await _sut.CheckAsync(feb.InvoiceId);

        result.IsEligible.Should().BeFalse();
        result.OldestUnpaidInvoiceId.Should().Be(overdueJan.InvoiceId);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task<Invoice> Seed(int tenantId, DateTime invoiceDate, decimal total, decimal paid, string status, string type = "Rent")
    {
        var inv = new Invoice
        {
            TenantId = tenantId,
            LeaseId = tenantId * 10, // any non-zero, distinct per tenant
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(5),
            TotalAmount = total,
            PaidAmount = paid,
            Status = status,
            InvoiceType = type,
            CreatedDate = invoiceDate,
        };
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync();
        return inv;
    }
}
