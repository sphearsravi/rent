using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services;
using RentCollection.Tests.Helpers;

namespace RentCollection.Tests.Jobs;

/// <summary>
/// Year-long calendar-march tests for <see cref="RecurringJobs"/>. Pin the
/// clock to Jan 1, advance month-by-month (or day-by-day for the lifecycle
/// suite), fire the relevant job at each step, and assert the long-running
/// properties — conservation, coverage, idempotency, exact-once flips.
///
/// These complement the single-shot tests in <see cref="RecurringJobsTests"/>:
/// those prove "given fixture X, the job produces row Y"; these prove
/// "given a full year of clock advances, the totals come out right" — which
/// is the property that matters in production but is impossible to assert
/// without a time seam.
/// </summary>
public class TimeTravelCalendarTests : IDisposable
{
    private static readonly DateTime YearStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _ctx;
    private readonly FixedClock _clock = new(YearStart);
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IExecutiveDashboardComposer> _composer = new();
    private readonly Mock<IPdfService> _pdf = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly IConfiguration _config;

    public TimeTravelCalendarTests()
    {
        _ctx = TestDatabaseHelper.CreateInMemoryContext($"TimeTravel_{Guid.NewGuid()}");
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reports:MonthlyEmailRecipients"] = "ops@alrtam.com",
            })
            .Build();

        _notifications.Setup(n => n.EnqueueTenantAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _composer.Setup(c => c.BuildAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutiveDashboardPdfModel
            {
                FromDate = YearStart, ToDate = YearStart.AddDays(30),
                CompanyName = "ALRTAM Company", Currency = "KWD",
            });
        _pdf.Setup(p => p.RenderExecutiveDashboard(It.IsAny<ExecutiveDashboardPdfModel>()))
            .Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF"
        _email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<EmailAttachment>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(true));
    }

    public void Dispose() => _ctx.Dispose();

    private RecurringJobs Sut() => new(
        _ctx, _notifications.Object, _composer.Object, _pdf.Object,
        _email.Object, _config, NullLogger<RecurringJobs>.Instance, _clock);

    // ───────────── Test 1: a year of rent invoicing ─────────────

    [Fact]
    public async Task Year_of_rent_invoicing_produces_exactly_twelve_invoices_per_active_lease()
    {
        // Two leases, both active all year.
        SeedLease(1, start: YearStart, end: YearStart.AddYears(1));
        SeedLease(2, start: YearStart, end: YearStart.AddYears(1));
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        // March one month at a time. On the 15th of each month, fire the
        // monthly job. Real Hangfire fires on the 1st, but the period
        // bounds are derived from the clock month either way.
        for (int monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            _clock.SetUtcNow(YearStart.AddMonths(monthIndex).AddDays(14));
            await sut.GenerateRentInvoicesAsync();
        }

        _ctx.Invoices.Count(i => i.InvoiceType == "Rent").Should().Be(24,
            "12 months × 2 active leases = 24 rent invoices");
        _ctx.Invoices.Count(i => i.InvoiceType == "Rent" && i.LeaseId == 1)
            .Should().Be(12, "each lease must have exactly one invoice per month");
    }

    [Fact]
    public async Task Year_of_rent_invoicing_is_idempotent_under_double_runs()
    {
        SeedLease(1, start: YearStart, end: YearStart.AddYears(1));
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        for (int monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            _clock.SetUtcNow(YearStart.AddMonths(monthIndex).AddDays(14));
            // Fire twice — once for the scheduled run, once to simulate the
            // "trigger now" button being clicked on the MEC page.
            await sut.GenerateRentInvoicesAsync();
            await sut.GenerateRentInvoicesAsync();
        }

        _ctx.Invoices.Count(i => i.InvoiceType == "Rent").Should().Be(12);
    }

    [Fact]
    public async Task Mid_year_lease_creation_starts_invoicing_from_lease_start_month()
    {
        // Lease starts April 1 — first invoice should be April, not Jan.
        var aprilFirst = new DateTime(2026, 4, 1);
        SeedLease(1, start: aprilFirst, end: YearStart.AddYears(1));
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        for (int monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            _clock.SetUtcNow(YearStart.AddMonths(monthIndex).AddDays(14));
            await sut.GenerateRentInvoicesAsync();
        }

        var rentInvoices = _ctx.Invoices
            .Where(i => i.InvoiceType == "Rent" && i.LeaseId == 1)
            .OrderBy(i => i.InvoiceDate)
            .Select(i => i.InvoiceDate.Month)
            .ToList();
        rentInvoices.Should().Equal(new[] { 4, 5, 6, 7, 8, 9, 10, 11, 12 },
            "Jan-Mar are before the lease starts, so 9 invoices Apr-Dec");
    }

    // ───────────── Test 2: a year of insurance cadences ─────────────

    [Fact]
    public async Task Year_of_insurance_Monthly_produces_twelve_Annual_produces_one_OneTime_produces_one()
    {
        var monthly = SeedLease(1, start: YearStart, end: YearStart.AddYears(1));
        monthly.InsuranceAmount = 25m;
        monthly.InsuranceFrequency = "Monthly";

        var annual = SeedLease(2, start: YearStart, end: YearStart.AddYears(1));
        annual.InsuranceAmount = 200m;
        annual.InsuranceFrequency = "Annual";
        annual.InsuranceStartDate = new DateTime(2026, 3, 1); // anchor: March

        var oneTime = SeedLease(3, start: YearStart, end: YearStart.AddYears(1));
        oneTime.InsuranceAmount = 500m;
        oneTime.InsuranceFrequency = "OneTime";

        await _ctx.SaveChangesAsync();
        var sut = Sut();

        for (int monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            _clock.SetUtcNow(YearStart.AddMonths(monthIndex).AddDays(14));
            await sut.GenerateInsuranceInvoicesAsync();
        }

        _ctx.Invoices.Count(i => i.InvoiceType == "Insurance" && i.LeaseId == 1)
            .Should().Be(12, "Monthly fires every period");
        _ctx.Invoices.Count(i => i.InvoiceType == "Insurance" && i.LeaseId == 2)
            .Should().Be(1, "Annual fires only in the anchor month (March)");
        _ctx.Invoices.Single(i => i.InvoiceType == "Insurance" && i.LeaseId == 2)
            .InvoiceDate.Month.Should().Be(3);
        _ctx.Invoices.Count(i => i.InvoiceType == "Insurance" && i.LeaseId == 3)
            .Should().Be(1, "OneTime fires exactly once across the entire year");
    }

    // ───────────── Test 3: lease lifecycle across a year ─────────────

    [Fact]
    public async Task Lease_ending_mid_year_flips_to_Expired_on_the_day_after_its_end_date()
    {
        var marchExpiry = SeedLease(1, start: YearStart, end: new DateTime(2026, 3, 15));
        var julyExpiry  = SeedLease(2, start: YearStart, end: new DateTime(2026, 7, 15));
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        // March day-by-day for the first 8 months. (We don't need to march
        // all 365 — the lifecycle of these two leases resolves by August.)
        DateTime? marchFlippedOn = null;
        DateTime? julyFlippedOn  = null;
        for (int dayOffset = 0; dayOffset < 240; dayOffset++)
        {
            _clock.SetUtcNow(YearStart.AddDays(dayOffset).AddHours(1));
            await sut.FlipExpiredLeasesAsync();

            // Re-read so EF returns the latest status from the change tracker.
            var l1 = _ctx.Leases.AsNoTracking().First(l => l.LeaseId == 1);
            var l2 = _ctx.Leases.AsNoTracking().First(l => l.LeaseId == 2);
            if (marchFlippedOn == null && l1.Status == "Expired") marchFlippedOn = _clock.Today;
            if (julyFlippedOn  == null && l2.Status == "Expired") julyFlippedOn  = _clock.Today;
        }

        marchFlippedOn.Should().Be(new DateTime(2026, 3, 16),
            "the flip job runs at 01:00; a lease ending 2026-03-15 has " +
            "LeaseEndDate < today first on 2026-03-16");
        julyFlippedOn.Should().Be(new DateTime(2026, 7, 16));
    }

    [Fact]
    public async Task Unit_reconciliation_marks_unit_vacant_after_its_lease_expires()
    {
        SeedUnit(1, "Occupied");
        var l = SeedLease(1, start: YearStart, end: new DateTime(2026, 3, 15));
        l.UnitId = 1;
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        // March 15 — lease still active, unit still Occupied.
        _clock.SetUtcNow(new DateTime(2026, 3, 15, 1, 0, 0, DateTimeKind.Utc));
        await sut.FlipExpiredLeasesAsync();
        await sut.ReconcileUnitStatusAsync();
        _ctx.Units.AsNoTracking().Single().Status.Should().Be("Occupied");

        // March 16 — flip fires first, then reconcile. Unit becomes Vacant.
        _clock.SetUtcNow(new DateTime(2026, 3, 16, 1, 0, 0, DateTimeKind.Utc));
        await sut.FlipExpiredLeasesAsync();
        await sut.ReconcileUnitStatusAsync();
        var unit = _ctx.Units.AsNoTracking().Single();
        unit.Status.Should().Be("Vacant");
        unit.AvailableFromDate.Should().NotBeNull();
    }

    // ───────────── Test 4: a year of monthly dashboard emails ─────────────

    [Fact]
    public async Task Year_of_monthly_dashboard_sends_one_email_per_month()
    {
        // No leases needed — the job sends an email to the configured
        // recipient regardless of data volume.
        var sut = Sut();

        for (int monthIndex = 0; monthIndex < 12; monthIndex++)
        {
            // Fire on the 1st of each month — when Hangfire schedules it.
            _clock.SetUtcNow(YearStart.AddMonths(monthIndex).AddHours(6));
            await sut.SendMonthlyExecutiveDashboardAsync();
        }

        _email.Verify(e => e.SendAsync(
                "ops@alrtam.com",
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<EmailAttachment>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(12));
    }

    // ───────────── Test 5: a year of expiry reminders ─────────────

    [Fact]
    public async Task Lease_expiry_reminders_fire_at_T60_T30_T7_exactly_once_each()
    {
        // Lease ending Aug 31 — should hit:
        //   T-60: July 2  (today + 60 = Aug 31)
        //   T-30: Aug 1
        //   T-7:  Aug 24
        var endDate = new DateTime(2026, 8, 31);
        SeedLease(1, start: YearStart, end: endDate);
        await _ctx.SaveChangesAsync();
        var sut = Sut();

        for (int dayOffset = 0; dayOffset < 240; dayOffset++)
        {
            _clock.SetUtcNow(YearStart.AddDays(dayOffset).AddHours(7));
            await sut.LeaseExpiryRemindersAsync();
        }

        // EnqueueTenantAsync for related Lease #1 should have been called
        // exactly 3 times (T-60, T-30, T-7).
        _notifications.Verify(n => n.EnqueueTenantAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), "Lease", 1, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    // ───────────── helpers ─────────────

    private Lease SeedLease(int leaseId, DateTime start, DateTime end)
    {
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
            LeaseStartDate = start,
            LeaseEndDate = end,
            MonthlyRent = 500m,
            Status = "Active",
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
