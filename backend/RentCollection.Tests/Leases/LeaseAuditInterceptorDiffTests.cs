using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Infrastructure.Services.Audit;
using Xunit;

namespace RentCollection.Tests.Leases;

/// <summary>
/// Unit tests for <see cref="LeaseAuditInterceptor.BuildDiff"/>. Uses an
/// in-memory DbContext so we can drive the EF change-tracker without
/// touching SQL Server.
/// </summary>
public class LeaseAuditInterceptorDiffTests
{
    private static ApplicationDbContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit-diff-{Guid.NewGuid()}")
            .Options;
        // Use the single-arg ctor so we don't have to mock ICompanyContext.
        // The InMemory provider ignores query filters anyway.
        return new ApplicationDbContext(opts);
    }

    [Fact]
    public void BuildDiff_returns_only_tracked_fields()
    {
        using var ctx = NewContext();
        var lease = new Lease
        {
            LeaseId = 1, TenantId = 10, UnitId = 100,
            LeaseStartDate = new DateTime(2026, 1, 1),
            LeaseEndDate = new DateTime(2026, 12, 31),
            MonthlyRent = 250m, PaymentDueDay = 1,
            Status = "Active",
        };
        ctx.Leases.Add(lease);
        ctx.SaveChanges();

        // Modify a tracked field + an untracked one
        lease.MonthlyRent = 275m;
        lease.ModifiedDate = DateTime.UtcNow;

        var entry = ctx.Entry(lease);
        var diff = LeaseAuditInterceptor.BuildDiff(entry);

        Assert.True(diff.ContainsKey(nameof(Lease.MonthlyRent)));
        Assert.False(diff.ContainsKey(nameof(Lease.ModifiedDate)),
            "ModifiedDate is excluded from the tracked fields list");
        Assert.Single(diff);
    }

    [Fact]
    public void BuildDiff_skips_Status_changes()
    {
        using var ctx = NewContext();
        var lease = new Lease
        {
            LeaseId = 1, TenantId = 10, UnitId = 100,
            LeaseStartDate = new DateTime(2026, 1, 1),
            LeaseEndDate = new DateTime(2026, 12, 31),
            MonthlyRent = 250m, PaymentDueDay = 1,
            Status = "Active",
        };
        ctx.Leases.Add(lease);
        ctx.SaveChanges();

        lease.Status = "Terminated";  // would double-log with lifecycle actions

        var diff = LeaseAuditInterceptor.BuildDiff(ctx.Entry(lease));
        Assert.Empty(diff);
    }

    [Fact]
    public void BuildDiff_captures_old_and_new_values()
    {
        using var ctx = NewContext();
        var lease = new Lease
        {
            LeaseId = 1, TenantId = 10, UnitId = 100,
            LeaseStartDate = new DateTime(2026, 1, 1),
            LeaseEndDate = new DateTime(2026, 12, 31),
            MonthlyRent = 250m, PaymentDueDay = 1,
            Status = "Active",
        };
        ctx.Leases.Add(lease);
        ctx.SaveChanges();

        lease.MonthlyRent = 275m;
        lease.LeaseEndDate = new DateTime(2027, 12, 31);

        var diff = LeaseAuditInterceptor.BuildDiff(ctx.Entry(lease));
        Assert.Equal(2, diff.Count);

        var rentDiff = diff[nameof(Lease.MonthlyRent)];
        Assert.Equal(250m, rentDiff[0]);
        Assert.Equal(275m, rentDiff[1]);

        var endDateDiff = diff[nameof(Lease.LeaseEndDate)];
        // Normalize() formats dates as yyyy-MM-dd.
        Assert.Equal("2026-12-31", endDateDiff[0]);
        Assert.Equal("2027-12-31", endDateDiff[1]);
    }

    [Fact]
    public void BuildDiff_returns_empty_when_no_tracked_field_changed()
    {
        using var ctx = NewContext();
        var lease = new Lease
        {
            LeaseId = 1, TenantId = 10, UnitId = 100,
            LeaseStartDate = new DateTime(2026, 1, 1),
            LeaseEndDate = new DateTime(2026, 12, 31),
            MonthlyRent = 250m, PaymentDueDay = 1,
            Status = "Active",
        };
        ctx.Leases.Add(lease);
        ctx.SaveChanges();

        // Touch only an untracked field.
        lease.ModifiedDate = DateTime.UtcNow;

        var diff = LeaseAuditInterceptor.BuildDiff(ctx.Entry(lease));
        Assert.Empty(diff);
    }
}
