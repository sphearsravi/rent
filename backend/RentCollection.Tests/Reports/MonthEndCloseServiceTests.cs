using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Reports;

/// <summary>
/// Tests for <see cref="MonthEndCloseService"/>. Each step has its own
/// scenario asserting the colour the aggregator produces for a given
/// seeded fixture. The aggregator returns an anonymous object the
/// controller serializes verbatim — we round-trip through JSON to keep
/// the test independent of the C# anonymous-type shape.
///
/// Period under test is fixed to 2026-05 so the assertions don't drift
/// with the calendar.
/// </summary>
public class MonthEndCloseServiceTests : IDisposable
{
    private const string Period = "2026-05";
    private static readonly DateTime ClockNow = new(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodStart = new(2026, 5, 1);
    private static readonly DateTime PeriodEnd = new(2026, 5, 31);

    private readonly ApplicationDbContext _ctx;
    private readonly MonthEndCloseService _service;

    public MonthEndCloseServiceTests()
    {
        var dbName = $"MEC_{Guid.NewGuid()}";
        _ctx = TestDatabaseHelper.CreateInMemoryContext(dbName);
        _service = new MonthEndCloseService(_ctx, new FixedClock(ClockNow));
    }

    public void Dispose() => _ctx.Dispose();

    /// <summary>
    /// MVC's default JsonOptions use camelCase, so the wire-format the view's
    /// JS actually sees is camelCase regardless of the C# anonymous-type
    /// casing. Mirror that here so the test asserts against the same shape
    /// production sends.
    /// </summary>
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private async Task<JsonElement> BuildAsync(string? period = Period)
    {
        var raw = await _service.BuildCloseStateAsync(period);
        var json = JsonSerializer.Serialize(raw, CamelCase);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Empty_database_yields_all_muted_summary()
    {
        var state = await BuildAsync();

        state.GetProperty("period").GetString().Should().Be("2026-05");
        state.GetProperty("steps").GetProperty("rentInvoices")
            .GetProperty("status").GetString().Should().Be("muted");
        state.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(9);
    }

    [Fact]
    public async Task Invalid_period_string_falls_back_to_current_month()
    {
        var state = await BuildAsync("not-a-period");

        // FixedClock pins now to 2026-06-05 → expected fallback period 2026-06
        state.GetProperty("period").GetString().Should().Be("2026-06");
    }

    [Fact]
    public async Task Rent_invoices_missing_for_active_leases_yields_red()
    {
        SeedLeases(2, withRentInvoiceInPeriod: 0);
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        var step = state.GetProperty("steps").GetProperty("rentInvoices");
        step.GetProperty("status").GetString().Should().Be("red");
        step.GetProperty("expected").GetInt32().Should().Be(2);
        step.GetProperty("issued").GetInt32().Should().Be(0);
        step.GetProperty("blockerHref").GetString().Should().Be("/Leases");
    }

    [Fact]
    public async Task Rent_invoices_complete_for_all_active_leases_yields_green()
    {
        SeedLeases(2, withRentInvoiceInPeriod: 2);
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        state.GetProperty("steps").GetProperty("rentInvoices")
            .GetProperty("status").GetString().Should().Be("green");
    }

    [Fact]
    public async Task Deposit_invoices_partial_yields_amber()
    {
        SeedLeases(3, withRentInvoiceInPeriod: 3, depositInvoiced: new[] { true, false, false });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        var step = state.GetProperty("steps").GetProperty("depositInvoices");
        step.GetProperty("status").GetString().Should().Be("amber");
        step.GetProperty("issued").GetInt32().Should().Be(1);
        step.GetProperty("expected").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Insurance_monthly_expected_but_not_issued_yields_amber()
    {
        var lease = SeedLeases(1, withRentInvoiceInPeriod: 1)[0];
        lease.InsuranceAmount = 25m;
        lease.InsuranceFrequency = "Monthly";
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        state.GetProperty("steps").GetProperty("insuranceInvoices")
            .GetProperty("status").GetString().Should().Be("amber");
    }

    [Fact]
    public async Task Overdue_invoices_left_unflipped_yields_amber()
    {
        // Seed with no rent invoices so the only Pending past-due row is the
        // one we add explicitly. (A May rent invoice with a 10-day grace
        // would also be past-due relative to ClockNow=2026-06-05 and would
        // skew the leftover count.)
        var lease = SeedLeases(1, withRentInvoiceInPeriod: 0)[0];
        _ctx.Invoices.Add(new Invoice
        {
            LeaseId = lease.LeaseId,
            TenantId = lease.TenantId,
            InvoiceDate = new DateTime(2026, 4, 1),
            DueDate = new DateTime(2026, 4, 15), // before ClockNow (2026-06-05)
            TotalAmount = 100m,
            Status = "Pending",
            InvoiceType = "Rent",
        });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        var step = state.GetProperty("steps").GetProperty("overdueFlip");
        step.GetProperty("status").GetString().Should().Be("amber");
        step.GetProperty("leftover").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Expired_active_leases_left_unflipped_yields_amber()
    {
        // Active lease whose end-date is before ClockNow
        _ctx.Tenants.Add(new Tenant
        {
            TenantId = 1,
            CompanyId = 1,
            FirstName = "Stale",
            LastName = "Lease",
            Email = "stale@example.com",
            Phone = "12345",
            GovernmentId = "ID1",
            Status = "Active",
        });
        _ctx.Leases.Add(new Lease
        {
            LeaseId = 99,
            UnitId = 1,
            TenantId = 1,
            LeaseStartDate = new DateTime(2025, 1, 1),
            LeaseEndDate = new DateTime(2026, 4, 30), // before ClockNow
            MonthlyRent = 200m,
            Status = "Active",
        });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        var step = state.GetProperty("steps").GetProperty("expiredLeaseFlip");
        step.GetProperty("status").GetString().Should().Be("amber");
        step.GetProperty("leftover").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Pending_bank_transfers_yield_amber()
    {
        _ctx.BankTransfers.Add(new BankTransfer
        {
            PaymentId = 1,
            TenantId = 1,
            PropertyId = 1,
            Amount = 500m,
            BankName = "NBK",
            TransferDate = new DateTime(2026, 5, 10),
            ProofFilePath = "x", ProofFileName = "x",
            Status = BankTransferStatus.PendingVerification,
        });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        var step = state.GetProperty("steps").GetProperty("bankTransferApprovals");
        step.GetProperty("status").GetString().Should().Be("amber");
        step.GetProperty("pending").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Knet_reconciliation_matched_in_period_yields_green()
    {
        _ctx.KNETReconciliations.Add(new KNETReconciliation
        {
            PaymentId = 1,
            PropertyId = 1,
            Amount = 250m,
            TransactionDate = new DateTime(2026, 5, 20),
            ReconciliationStatus = "Matched",
        });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        state.GetProperty("steps").GetProperty("knetReconciliation")
            .GetProperty("status").GetString().Should().Be("green");
    }

    [Fact]
    public async Task Executive_dashboard_emailed_audit_yields_green()
    {
        _ctx.AuditLogs.Add(new AuditLog
        {
            EntityType = "Report",
            EntityId = 0,
            Action = "MonthlyDashboardEmailed",
            ByUser = "system",
            At = new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc), // within grace window after period end
        });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        state.GetProperty("steps").GetProperty("executiveDashboardEmail")
            .GetProperty("status").GetString().Should().Be("green");
    }

    [Fact]
    public async Task Closed_period_payload_carries_close_metadata()
    {
        _ctx.ClosedPeriods.Add(new ClosedPeriod
        {
            Period = Period,
            ClosedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            ClosedBy = "admin@test.com",
            Notes = "Books are shut.",
        });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        var closed = state.GetProperty("closed");
        closed.ValueKind.Should().Be(JsonValueKind.Object);
        closed.GetProperty("closedBy").GetString().Should().Be("admin@test.com");
        closed.GetProperty("notes").GetString().Should().Be("Books are shut.");
    }

    [Fact]
    public async Task Summary_tallies_match_step_colours()
    {
        // 1 lease, no rent invoice in period → red rent
        SeedLeases(1, withRentInvoiceInPeriod: 0);
        // Pending bank transfer → amber transfers
        _ctx.BankTransfers.Add(new BankTransfer
        {
            PaymentId = 1, TenantId = 1, PropertyId = 1, Amount = 500m,
            BankName = "NBK", TransferDate = new DateTime(2026, 5, 10),
            ProofFilePath = "x", ProofFileName = "x",
            Status = BankTransferStatus.PendingVerification,
        });
        await _ctx.SaveChangesAsync();

        var state = await BuildAsync();

        var summary = state.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(9);
        summary.GetProperty("red").GetInt32().Should().BeGreaterThan(0);
        summary.GetProperty("amber").GetInt32().Should().BeGreaterThan(0);
        (summary.GetProperty("green").GetInt32()
            + summary.GetProperty("amber").GetInt32()
            + summary.GetProperty("red").GetInt32()
            + summary.GetProperty("muted").GetInt32())
            .Should().Be(9);
    }

    // -------- helpers --------

    /// <summary>
    /// Seed <paramref name="count"/> active leases for the test period, optionally
    /// generating <paramref name="withRentInvoiceInPeriod"/> matching Rent invoices.
    /// Each lease has SecurityDeposit set so deposit-invoice math has meaningful
    /// expected counts; <paramref name="depositInvoiced"/> controls per-lease flag.
    /// </summary>
    private List<Lease> SeedLeases(int count, int withRentInvoiceInPeriod, bool[]? depositInvoiced = null)
    {
        // Use Local for the existence probe — see note in RecurringJobsTests.
        if (!_ctx.Companies.Local.Any() && !_ctx.Companies.Any())
        {
            _ctx.Companies.Add(new Company
            {
                CompanyId = 1, CompanyName = "T", Email = "t@t.com",
                Phone = "1", Address = "x", IsActive = true,
            });
        }
        var leases = new List<Lease>();
        for (int i = 0; i < count; i++)
        {
            var tenantId = 100 + i;
            _ctx.Tenants.Add(new Tenant
            {
                TenantId = tenantId, CompanyId = 1,
                FirstName = $"T{i}", LastName = "X",
                Email = $"t{i}@x.com", Phone = "1",
                GovernmentId = $"GID{i}",
                Status = "Active",
            });
            var lease = new Lease
            {
                LeaseId = 1000 + i,
                UnitId = i + 1,
                TenantId = tenantId,
                LeaseStartDate = PeriodStart.AddMonths(-2),
                LeaseEndDate = PeriodEnd.AddMonths(6),
                MonthlyRent = 500m,
                SecurityDeposit = 500m,
                Status = "Active",
                DepositInvoiced = depositInvoiced != null && i < depositInvoiced.Length
                    ? depositInvoiced[i] : false,
            };
            _ctx.Leases.Add(lease);
            leases.Add(lease);

            if (i < withRentInvoiceInPeriod)
            {
                _ctx.Invoices.Add(new Invoice
                {
                    LeaseId = lease.LeaseId,
                    TenantId = tenantId,
                    InvoiceDate = PeriodStart.AddDays(1),
                    DueDate = PeriodStart.AddDays(10),
                    TotalAmount = 500m,
                    Status = "Pending",
                    InvoiceType = "Rent",
                });
            }
        }
        return leases;
    }
}
