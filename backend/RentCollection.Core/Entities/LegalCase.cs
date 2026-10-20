namespace RentCollection.Core.Entities;

/// <summary>
/// A legal / court case filed against a lease (eviction, non-payment, damages…).
/// The case is filed on a specific <see cref="Lease"/>; <see cref="TenantId"/> is
/// denormalized so "does this tenant have any case?" survives lease termination.
/// Only a lean, status-tracking subset is exposed to the tenant app — internal
/// <see cref="Notes"/> and <see cref="AmountClaimed"/> stay staff-only.
/// </summary>
public class LegalCase
{
    public int LegalCaseId { get; set; }

    // Company scope — mirrors Tenant/Property so the global query filter applies.
    public int CompanyId { get; set; }

    // The case is filed on this lease; tenant denormalized for direct lookups.
    public int LeaseId { get; set; }
    public Lease Lease { get; set; } = null!;
    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string? CaseNumber { get; set; }                       // court reference, e.g. "2026/1234"
    public string CaseType { get; set; } = LegalCaseTypes.Other;  // Eviction/NonPayment/Damages/Other
    public string Status { get; set; } = LegalCaseStatuses.Open;  // Open/Hearing/Won/Lost/Settled/Withdrawn/Closed

    public DateTime FiledDate { get; set; } = DateTime.UtcNow;
    public DateTime? NextHearingDate { get; set; }
    public decimal? AmountClaimed { get; set; }                   // KWD — staff-only
    public string? Notes { get; set; }                            // staff-only

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>An open case is one still in progress (drives the tenant banner + badge).</summary>
    public bool IsOpen => Status is not (LegalCaseStatuses.Won
        or LegalCaseStatuses.Lost or LegalCaseStatuses.Settled
        or LegalCaseStatuses.Withdrawn or LegalCaseStatuses.Closed);
}

public static class LegalCaseTypes
{
    public const string Eviction = "Eviction";
    public const string NonPayment = "NonPayment";
    public const string Damages = "Damages";
    public const string Other = "Other";

    public static readonly string[] All = { Eviction, NonPayment, Damages, Other };
}

public static class LegalCaseStatuses
{
    public const string Open = "Open";
    public const string Hearing = "Hearing";
    public const string Won = "Won";
    public const string Lost = "Lost";
    public const string Settled = "Settled";
    public const string Withdrawn = "Withdrawn";
    public const string Closed = "Closed";

    public static readonly string[] All = { Open, Hearing, Won, Lost, Settled, Withdrawn, Closed };
}
