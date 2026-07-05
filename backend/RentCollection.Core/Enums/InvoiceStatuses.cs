namespace RentCollection.Core.Enums;

/// <summary>
/// Canonical strings for Invoice.Status. Use these constants instead of magic strings
/// so the compiler catches typos and the IDE finds usages.
/// </summary>
public static class InvoiceStatuses
{
    public const string Pending = "Pending";
    public const string PartiallyPaid = "Partially Paid";
    public const string Paid = "Paid";
    public const string Overdue = "Overdue";
    public const string Cancelled = "Cancelled";
}

// LeaseStatuses now lives next to Lease.cs in RentCollection.Core.Entities
// to keep the lifecycle constants close to the entity.

/// <summary>
/// Canonical strings for <c>Invoice.InvoiceType</c>. Each value gets its
/// own invoice row (no line-item collapsing) so reports can sum / filter
/// by category cleanly. Rent is the high-volume default; the others are
/// issued less frequently by the appropriate domain action.
/// </summary>
public static class InvoiceTypes
{
    public const string Rent = "Rent";
    public const string Deposit = "Deposit";
    public const string Insurance = "Insurance";
    public const string LateFee = "LateFee";
    public const string MaintenanceFee = "MaintenanceFee";
    public const string Other = "Other";

    public static readonly string[] All =
        { Rent, Deposit, Insurance, LateFee, MaintenanceFee, Other };
}

/// <summary>
/// Cadence for the optional insurance charge on a Lease. Drives the monthly
/// Hangfire job: Monthly fires every period, Annual fires once per year on
/// the anniversary of <c>InsuranceStartDate</c>, OneTime fires exactly once
/// (idempotent via NOT EXISTS).
/// </summary>
public static class InsuranceFrequencies
{
    public const string None = "None";
    public const string Monthly = "Monthly";
    public const string Annual = "Annual";
    public const string OneTime = "OneTime";

    public static readonly string[] All = { None, Monthly, Annual, OneTime };
}

/// <summary>
/// Canonical strings for Tenant.Status.
/// </summary>
public static class TenantStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Suspended = "Suspended";
}

/// <summary>
/// Canonical taxonomy for <c>Unit.UnitType</c>. The free-text column accepts
/// anything; using these constants in Create/Edit dropdowns keeps the value
/// space tractable so chip filters + reports work. Existing rows
/// ("Apartment", "Commercial", "Penthouse") already match these strings.
/// </summary>
public static class UnitTypes
{
    public const string Apartment = "Apartment";
    public const string Studio = "Studio";
    public const string Office = "Office";
    public const string Shop = "Shop";
    public const string Commercial = "Commercial";
    public const string Penthouse = "Penthouse";
    public const string Warehouse = "Warehouse";
    public const string Other = "Other";

    public static readonly string[] All =
        { Apartment, Studio, Office, Shop, Commercial, Penthouse, Warehouse, Other };
}

/// <summary>
/// Canonical strings for <c>Unit.Status</c>. <c>Vacant</c>/<c>Occupied</c>
/// are auto-managed by the daily ReconcileUnitStatus Hangfire job; the
/// other two are operator-set holds.
/// </summary>
public static class UnitStatuses
{
    public const string Vacant = "Vacant";        // ready to lease
    public const string Occupied = "Occupied";    // active lease in place
    public const string Maintenance = "Maintenance"; // under repair, do not lease
    public const string Reserved = "Reserved";    // held for a viewer, hidden from vacancy pool

    public static readonly string[] All = { Vacant, Occupied, Maintenance, Reserved };

    /// <summary>True when the daily reconciliation job is allowed to flip
    /// this status. <c>Maintenance</c> and <c>Reserved</c> are explicit
    /// holds — the job leaves them alone so the operator's intent isn't
    /// silently overwritten.</summary>
    public static bool IsAutoManaged(string status)
        => status == Vacant || status == Occupied;
}

/// <summary>
/// Derived size band — computed from <c>Unit.Bedrooms</c> + <c>UnitType</c>
/// during create/edit so chip filters work without operators having to set
/// it manually. Stored on the row so reports can filter without recomputing.
/// </summary>
public static class UnitSizeBands
{
    public const string Studio = "Studio";
    public const string OneBhk = "1BHK";
    public const string TwoBhk = "2BHK";
    public const string ThreeBhkPlus = "3BHK+";
    public const string Commercial = "Commercial";

    public static readonly string[] All =
        { Studio, OneBhk, TwoBhk, ThreeBhkPlus, Commercial };

    /// <summary>Pure function used by both the Create/Edit save path and the
    /// migration backfill. Identical input → identical output.</summary>
    public static string Derive(int? bedrooms, string? unitType)
    {
        if (unitType is "Commercial" or "Shop" or "Office" or "Warehouse")
            return Commercial;
        return bedrooms switch
        {
            null or 0 => Studio,
            1 => OneBhk,
            2 => TwoBhk,
            _ => ThreeBhkPlus,
        };
    }
}
