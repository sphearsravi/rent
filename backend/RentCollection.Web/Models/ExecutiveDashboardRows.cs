namespace RentCollection.Web.Models;

/// <summary>
/// One row of the Executive report's "Property health" table. Occupancy comes
/// from the unit statuses; Invoiced / Collected are the period's invoice totals
/// (TotalAmount / PaidAmount) attributed through Invoice → Lease → Unit → Property,
/// so the rows sum to the same figures the hero card shows.
/// </summary>
public class ExecutivePropertyRow
{
    public int PropertyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalUnits { get; set; }
    public int OccupiedUnits { get; set; }
    public decimal Invoiced { get; set; }
    public decimal Collected { get; set; }

    public decimal OccupancyRate => TotalUnits > 0 ? (decimal)OccupiedUnits / TotalUnits * 100 : 0;

    /// <summary>Null when nothing was invoiced in the period — there is no rate to grade.</summary>
    public decimal? CollectionRate => Invoiced > 0 ? Math.Min(100, Collected / Invoiced * 100) : null;
}

/// <summary>
/// One calendar month of the Executive report's collections trend. Bar heights
/// are computed server-side (px out of <see cref="MaxPx"/>) so the view stays
/// free of JS and the two bars share one scale.
/// </summary>
public class ExecutiveTrendPoint
{
    public const int MaxPx = 160;

    public string Label { get; set; } = string.Empty;
    public decimal Expected { get; set; }
    public decimal Collected { get; set; }
    public int ExpectedPx { get; set; }
    public int CollectedPx { get; set; }
    /// <summary>True for the current calendar month — the collected bar is drawn as partial.</summary>
    public bool IsPartial { get; set; }

    /// <summary>Scale a value against the series maximum; never below a 2px hairline so a zero month still reads.</summary>
    public static int ToPx(decimal value, decimal max)
        => max <= 0 || value <= 0 ? 2 : Math.Max(2, (int)Math.Round(value / max * MaxPx));
}
