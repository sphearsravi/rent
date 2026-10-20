using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RentCollection.Application.Interfaces;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Tests.Helpers;
using RentCollection.Web.Controllers;

namespace RentCollection.Tests.Leases;

/// <summary>
/// One tenant per unit per period. Reproduces the production incident: a
/// unit with a Pending (deposit) lease stayed "Vacant" and accepted a second,
/// overlapping lease. The guard is period-aware so pre-leasing a unit for
/// after the current lease ends stays possible.
/// </summary>
public class LeaseOverlapGuardTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public LeaseOverlapGuardTests()
    {
        _context = TestDatabaseHelper.CreateInMemoryContextWithTransactionsIgnored(Guid.NewGuid().ToString());
    }

    public void Dispose() => _context.Dispose();

    private LeasesController Controller(string role = Roles.Admin)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(Path.GetTempPath());
        var c = new LeasesController(_context, Mock.Of<IAuditLogService>(), env.Object, Mock.Of<ILogger<LeasesController>>());
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "tester"),
                new Claim(ClaimTypes.Role, role),
                new Claim("companyId", "1"),
            }, "Test")),
        };
        c.ControllerContext = new ControllerContext { HttpContext = http };
        c.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return c;
    }

    private static Lease NewLease(int unitId, int tenantId, DateTime start, DateTime end, decimal deposit = 0) => new()
    {
        UnitId = unitId,
        TenantId = tenantId,
        LeaseStartDate = start,
        LeaseEndDate = end,
        MonthlyRent = 450,
        SecurityDeposit = deposit,
        PaymentDueDay = 1,
        LeaseType = "Fixed",
        InsuranceFrequency = "None",
    };

    /// <summary>A vacant unit (id 5) in building 1 with no leases at all.</summary>
    private async Task SeedVacantUnitAsync()
    {
        _context.Units.Add(new Unit
        {
            UnitId = 5, PropertyId = 1, UnitNumber = "105", UnitType = "Apartment",
            MonthlyRent = 400, Status = "Vacant", CreatedDate = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_on_unit_with_overlapping_Active_lease_is_rejected()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var before = await _context.Leases.CountAsync();

        // Unit 1 is held by lease 1 (tenant 1, −6m .. +6m). Tenant 2 wants it from today.
        var result = await Controller().Create(NewLease(1, 2, DateTime.Today, DateTime.Today.AddYears(1)));

        result.Should().BeOfType<ViewResult>("the form is re-rendered with the conflict");
        var ctrl = (ViewResult)result;
        ctrl.ViewData.ModelState.IsValid.Should().BeFalse();
        ctrl.ViewData.ModelState[nameof(Lease.UnitId)]!.Errors.Single().ErrorMessage
            .Should().Contain("#1").And.Contain("Ahmed Al-Rashid").And.Contain("active lease");
        (await _context.Leases.CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Create_on_unit_with_Pending_lease_is_rejected_the_production_bug()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedVacantUnitAsync();

        // First lease WITH a deposit → lands as Pending, unit stays "Vacant".
        var first = await Controller().Create(NewLease(5, 1, DateTime.Today, DateTime.Today.AddYears(1).AddDays(-1), deposit: 400));
        first.Should().BeOfType<RedirectToActionResult>();
        var pending = await _context.Leases.SingleAsync(l => l.UnitId == 5);
        pending.Status.Should().Be(LeaseStatuses.Pending);
        (await _context.Units.FindAsync(5))!.Status.Should().Be("Vacant", "Pending never flipped the unit — that was the hole");

        // Second lease on the same unit, same period, no deposit → used to go straight to Active.
        var second = await Controller().Create(NewLease(5, 2, DateTime.Today, DateTime.Today.AddYears(1).AddDays(-1)));

        second.Should().BeOfType<ViewResult>();
        ((ViewResult)second).ViewData.ModelState[nameof(Lease.UnitId)]!.Errors.Single().ErrorMessage
            .Should().Contain("pending lease").And.Contain($"#{pending.LeaseId}");
        (await _context.Leases.CountAsync(l => l.UnitId == 5)).Should().Be(1);
    }

    [Fact]
    public async Task Create_for_period_after_current_lease_ends_is_allowed()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var lease1End = (await _context.Leases.FindAsync(1))!.LeaseEndDate;

        // Pre-lease unit 1 to tenant 2 starting the day after lease 1 ends.
        var result = await Controller().Create(NewLease(1, 2, lease1End.AddDays(1), lease1End.AddYears(1)));

        result.Should().BeOfType<RedirectToActionResult>();
        (await _context.Leases.CountAsync(l => l.UnitId == 1)).Should().Be(2);
    }

    [Fact]
    public async Task Create_on_closed_lease_unit_is_allowed()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var old = await _context.Leases.FindAsync(1);
        old!.Status = LeaseStatuses.Terminated;
        await _context.SaveChangesAsync();

        var result = await Controller().Create(NewLease(1, 2, DateTime.Today, DateTime.Today.AddYears(1)));

        result.Should().BeOfType<RedirectToActionResult>("a terminated lease no longer claims the unit");
    }

    [Fact]
    public async Task Activate_is_refused_when_another_live_lease_took_the_unit()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        await SeedVacantUnitAsync();
        // Draft (Pending) lease for tenant 1, then — bypassing the guard, as
        // legacy data did — an Active lease for tenant 2 on the same unit.
        var pending = NewLease(5, 1, DateTime.Today, DateTime.Today.AddYears(1), deposit: 400);
        pending.Status = LeaseStatuses.Pending;
        var active = NewLease(5, 2, DateTime.Today, DateTime.Today.AddYears(1));
        active.Status = LeaseStatuses.Active;
        _context.Leases.AddRange(pending, active);
        await _context.SaveChangesAsync();

        var ctrl = Controller();
        var result = await ctrl.Activate(pending.LeaseId);

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Details");
        ctrl.TempData["ErrorMessage"]!.ToString().Should().StartWith("Cannot activate:").And.Contain($"#{active.LeaseId}");
        (await _context.Leases.AsNoTracking().SingleAsync(l => l.LeaseId == pending.LeaseId)).Status
            .Should().Be(LeaseStatuses.Pending);
    }

    [Fact]
    public async Task Edit_cannot_extend_into_a_preleased_period()
    {
        await TestDatabaseHelper.SeedTestDataAsync(_context);
        var current = await _context.Leases.AsNoTracking().SingleAsync(l => l.LeaseId == 1);
        // Pre-lease unit 1 to tenant 2 right after lease 1.
        var next = NewLease(1, 2, current.LeaseEndDate.AddDays(1), current.LeaseEndDate.AddYears(1));
        next.Status = LeaseStatuses.Pending;
        _context.Leases.Add(next);
        await _context.SaveChangesAsync();

        // Now try to push lease 1's end date one month into the pre-leased period.
        var edited = new Lease
        {
            LeaseId = 1, TenantId = current.TenantId,
            LeaseStartDate = current.LeaseStartDate,
            LeaseEndDate = current.LeaseEndDate.AddMonths(1),
            MonthlyRent = current.MonthlyRent, SecurityDeposit = current.SecurityDeposit,
            PaymentDueDay = current.PaymentDueDay, LeaseType = current.LeaseType,
            InsuranceFrequency = current.InsuranceFrequency, RowVersion = current.RowVersion,
        };
        var result = await Controller().Edit(1, edited);

        result.Should().BeOfType<ViewResult>();
        ((ViewResult)result).ViewData.ModelState[nameof(Lease.LeaseEndDate)]!.Errors.Single().ErrorMessage
            .Should().Contain($"#{next.LeaseId}");
        (await _context.Leases.AsNoTracking().SingleAsync(l => l.LeaseId == 1)).LeaseEndDate
            .Should().Be(current.LeaseEndDate);
    }
}
