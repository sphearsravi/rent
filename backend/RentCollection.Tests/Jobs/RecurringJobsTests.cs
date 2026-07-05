using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Jobs;

/// <summary>
/// Behaviour tests for the seven Hangfire recurring jobs in
/// <see cref="RecurringJobs"/>. Each test pins "now" via <see cref="FixedClock"/>
/// so the calendar-driven branches (period bounds, expiry windows) are
/// deterministic.
///
/// The job's collaborators (<see cref="INotificationService"/>,
/// <see cref="IExecutiveDashboardComposer"/>, <see cref="IPdfService"/>,
/// <see cref="IEmailService"/>) are mocked with Moq because the jobs delegate
/// to them but their internals aren't what we're asserting here.
///
/// Two properties matter most:
///   1. Correctness — given fixture X, the job produces exactly Y new rows.
///   2. Idempotency — running the same job twice on the same clock yields no
///      additional side effects.
/// </summary>
public class RecurringJobsTests : IDisposable
{
    private static readonly DateTime MidPeriodMay = new(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _ctx;
    private readonly FixedClock _clock = new(MidPeriodMay);
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IExecutiveDashboardComposer> _composer = new();
    private readonly Mock<IPdfService> _pdf = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly IConfiguration _config;
    private readonly ILogger<RecurringJobs> _logger = NullLogger<RecurringJobs>.Instance;

    public RecurringJobsTests()
    {
        _ctx = TestDatabaseHelper.CreateInMemoryContext($"Jobs_{Guid.NewGuid()}");
        _config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        _notifications.Setup(n => n.EnqueueTenantAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<EmailAttachment>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(true));
    }

    public void Dispose() => _ctx.Dispose();

    private RecurringJobs Sut() => new(
        _ctx, _notifications.Object, _composer.Object, _pdf.Object,
        _email.Object, _config, _logger, _clock);

    // ───────────── GenerateRentInvoicesAsync ─────────────

    [Fact]
    public async Task GenerateRent_creates_one_invoice_per_active_lease()
    {
        SeedLease(leaseId: 1, status: "Active", rent: 500m);
        SeedLease(leaseId: 2, status: "Active", rent: 750m);
        await _ctx.SaveChangesAsync();

        await Sut().GenerateRentInvoicesAsync();

        _ctx.Invoices.Count(i => i.InvoiceType == "Rent").Should().Be(2);
        _ctx.Invoices.Single(i => i.LeaseId == 1).TotalAmount.Should().Be(500m);
    }

    [Fact]
    public async Task GenerateRent_is_idempotent_across_two_runs_in_the_same_period()
    {
        SeedLease(1, status: "Active", rent: 500m);
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        await sut.GenerateRentInvoicesAsync();
        await sut.GenerateRentInvoicesAsync();

        _ctx.Invoices.Count(i => i.InvoiceType == "Rent" && i.LeaseId == 1).Should().Be(1);
    }

    [Fact]
    public async Task GenerateRent_skips_expired_and_pending_leases()
    {
        SeedLease(1, status: "Active",  rent: 500m);
        SeedLease(2, status: "Expired", rent: 500m);
        SeedLease(3, status: "Pending", rent: 500m);
        await _ctx.SaveChangesAsync();

        await Sut().GenerateRentInvoicesAsync();

        _ctx.Invoices.Should().HaveCount(1);
        _ctx.Invoices.Single().LeaseId.Should().Be(1);
    }

    [Fact]
    public async Task GenerateRent_enqueues_a_notification_per_invoice()
    {
        SeedLease(1, status: "Active", rent: 500m);
        await _ctx.SaveChangesAsync();

        await Sut().GenerateRentInvoicesAsync();

        _notifications.Verify(n => n.EnqueueTenantAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "Invoice", It.IsAny<int?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ───────────── GenerateInsuranceInvoicesAsync ─────────────

    [Fact]
    public async Task GenerateInsurance_creates_invoice_for_monthly_lease()
    {
        var l = SeedLease(1, status: "Active", rent: 500m);
        l.InsuranceAmount = 25m;
        l.InsuranceFrequency = "Monthly";
        await _ctx.SaveChangesAsync();

        await Sut().GenerateInsuranceInvoicesAsync();

        _ctx.Invoices.Count(i => i.InvoiceType == "Insurance" && i.TotalAmount == 25m)
            .Should().Be(1);
    }

    [Fact]
    public async Task GenerateInsurance_skips_None_frequency_even_when_amount_set()
    {
        var l = SeedLease(1, status: "Active", rent: 500m);
        l.InsuranceAmount = 25m;
        l.InsuranceFrequency = "None";
        await _ctx.SaveChangesAsync();

        await Sut().GenerateInsuranceInvoicesAsync();

        _ctx.Invoices.Any(i => i.InvoiceType == "Insurance").Should().BeFalse();
    }

    [Fact]
    public async Task GenerateInsurance_annual_fires_only_in_anchor_month()
    {
        // FixedClock is in May; annual anchor in March should NOT fire
        var l = SeedLease(1, status: "Active", rent: 500m);
        l.InsuranceAmount = 100m;
        l.InsuranceFrequency = "Annual";
        l.InsuranceStartDate = new DateTime(2026, 3, 1);
        await _ctx.SaveChangesAsync();

        await Sut().GenerateInsuranceInvoicesAsync();

        _ctx.Invoices.Any(i => i.InvoiceType == "Insurance").Should().BeFalse();
    }

    [Fact]
    public async Task GenerateInsurance_one_time_fires_once_and_never_again()
    {
        var l = SeedLease(1, status: "Active", rent: 500m);
        l.InsuranceAmount = 100m;
        l.InsuranceFrequency = "OneTime";
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        await sut.GenerateInsuranceInvoicesAsync();
        await sut.GenerateInsuranceInvoicesAsync();
        // Advance a month and run again — still no duplicate.
        _clock.Advance(TimeSpan.FromDays(31));
        await sut.GenerateInsuranceInvoicesAsync();

        _ctx.Invoices.Count(i => i.InvoiceType == "Insurance").Should().Be(1);
    }

    // ───────────── FlipOverdueInvoicesAsync ─────────────

    [Fact]
    public async Task FlipOverdue_flips_pending_invoices_past_due()
    {
        SeedLease(1, status: "Active", rent: 500m);
        _ctx.Invoices.Add(new Invoice
        {
            LeaseId = 1, TenantId = 100,
            InvoiceDate = new DateTime(2026, 4, 1),
            DueDate = new DateTime(2026, 4, 15), // before clock (May 15)
            TotalAmount = 500m, Status = "Pending", InvoiceType = "Rent",
        });
        await _ctx.SaveChangesAsync();

        await Sut().FlipOverdueInvoicesAsync();

        _ctx.Invoices.Single().Status.Should().Be("Overdue");
    }

    [Fact]
    public async Task FlipOverdue_leaves_future_due_dates_pending()
    {
        SeedLease(1, status: "Active", rent: 500m);
        _ctx.Invoices.Add(new Invoice
        {
            LeaseId = 1, TenantId = 100,
            InvoiceDate = new DateTime(2026, 5, 1),
            DueDate = new DateTime(2026, 5, 31), // after clock (May 15)
            TotalAmount = 500m, Status = "Pending", InvoiceType = "Rent",
        });
        await _ctx.SaveChangesAsync();

        await Sut().FlipOverdueInvoicesAsync();

        _ctx.Invoices.Single().Status.Should().Be("Pending");
    }

    [Fact]
    public async Task FlipOverdue_is_idempotent()
    {
        SeedLease(1, status: "Active", rent: 500m);
        _ctx.Invoices.Add(new Invoice
        {
            LeaseId = 1, TenantId = 100,
            InvoiceDate = new DateTime(2026, 4, 1),
            DueDate = new DateTime(2026, 4, 15),
            TotalAmount = 500m, Status = "Pending", InvoiceType = "Rent",
        });
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        await sut.FlipOverdueInvoicesAsync();
        await sut.FlipOverdueInvoicesAsync();

        // Second run finds no Pending past-due rows → no second notification.
        _notifications.Verify(n => n.EnqueueTenantAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "Invoice", It.IsAny<int?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ───────────── FlipExpiredLeasesAsync ─────────────

    [Fact]
    public async Task FlipExpired_flips_active_leases_past_end_date()
    {
        var l = SeedLease(1, status: "Active", rent: 500m);
        l.LeaseStartDate = new DateTime(2025, 1, 1);
        l.LeaseEndDate = new DateTime(2026, 4, 30); // before clock
        await _ctx.SaveChangesAsync();

        await Sut().FlipExpiredLeasesAsync();

        _ctx.Leases.Single().Status.Should().Be("Expired");
    }

    [Fact]
    public async Task FlipExpired_leaves_current_leases_active()
    {
        var l = SeedLease(1, status: "Active", rent: 500m);
        l.LeaseEndDate = new DateTime(2026, 12, 31);
        await _ctx.SaveChangesAsync();

        await Sut().FlipExpiredLeasesAsync();

        _ctx.Leases.Single().Status.Should().Be("Active");
    }

    // ───────────── ReconcileUnitStatusAsync ─────────────

    [Fact]
    public async Task ReconcileUnit_marks_unit_with_active_lease_as_occupied()
    {
        SeedUnit(1, status: "Vacant");
        var l = SeedLease(1, status: "Active", rent: 500m);
        l.UnitId = 1;
        await _ctx.SaveChangesAsync();

        await Sut().ReconcileUnitStatusAsync();

        _ctx.Units.Single().Status.Should().Be("Occupied");
    }

    [Fact]
    public async Task ReconcileUnit_marks_unit_without_active_lease_as_vacant()
    {
        SeedUnit(1, status: "Occupied");
        // Expired lease only — no active lease exists for this unit.
        var l = SeedLease(1, status: "Expired", rent: 500m);
        l.UnitId = 1;
        await _ctx.SaveChangesAsync();

        await Sut().ReconcileUnitStatusAsync();

        var unit = _ctx.Units.Single();
        unit.Status.Should().Be("Vacant");
        unit.AvailableFromDate.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task ReconcileUnit_leaves_maintenance_and_reserved_alone()
    {
        SeedUnit(1, status: "Maintenance");
        SeedUnit(2, status: "Reserved");
        await _ctx.SaveChangesAsync();

        await Sut().ReconcileUnitStatusAsync();

        _ctx.Units.First(u => u.UnitId == 1).Status.Should().Be("Maintenance");
        _ctx.Units.First(u => u.UnitId == 2).Status.Should().Be("Reserved");
    }

    // ───────────── helpers ─────────────

    private Lease SeedLease(int leaseId, string status, decimal rent)
    {
        // Use Local for the existence probe — in-memory `Any()` queries the
        // store, not the change tracker, so prior un-saved Adds aren't seen
        // and we'd duplicate-key on the second call to SeedLease.
        if (!_ctx.Companies.Local.Any() && !_ctx.Companies.Any())
        {
            _ctx.Companies.Add(new Company
            {
                CompanyId = 1, CompanyName = "T", Email = "t@t.com",
                Phone = "1", Address = "x", IsActive = true,
            });
        }
        var tenantId = 100 + leaseId - 1;
        if (!_ctx.Tenants.Local.Any(t => t.TenantId == tenantId)
            && !_ctx.Tenants.Any(t => t.TenantId == tenantId))
        {
            _ctx.Tenants.Add(new Tenant
            {
                TenantId = tenantId, CompanyId = 1,
                FirstName = $"T{leaseId}", LastName = "X",
                Email = $"t{leaseId}@x.com", Phone = "1",
                GovernmentId = $"GID{leaseId}", Status = "Active",
            });
        }
        var lease = new Lease
        {
            LeaseId = leaseId,
            UnitId = leaseId,
            TenantId = tenantId,
            LeaseStartDate = new DateTime(2026, 1, 1),
            LeaseEndDate = new DateTime(2027, 1, 1),
            MonthlyRent = rent,
            Status = status,
        };
        _ctx.Leases.Add(lease);
        return lease;
    }

    private Unit SeedUnit(int unitId, string status)
    {
        if (!_ctx.Properties.Local.Any(p => p.PropertyId == 1)
            && !_ctx.Properties.Any(p => p.PropertyId == 1))
        {
            if (!_ctx.Companies.Local.Any() && !_ctx.Companies.Any())
            {
                _ctx.Companies.Add(new Company
                {
                    CompanyId = 1, CompanyName = "T", Email = "t@t.com",
                    Phone = "1", Address = "x", IsActive = true,
                });
            }
            _ctx.Properties.Add(new Property
            {
                PropertyId = 1, CompanyId = 1, PropertyName = "P",
                PropertyType = "Residential", Address = "x", Status = "Active",
            });
        }
        var unit = new Unit
        {
            UnitId = unitId, PropertyId = 1,
            UnitNumber = $"U{unitId}", UnitType = "Apartment",
            Status = status,
        };
        _ctx.Units.Add(unit);
        return unit;
    }
}
