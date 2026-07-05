namespace RentCollection.Core.Entities;

public class Lease
{
    public int LeaseId { get; set; }
    public int UnitId { get; set; }
    public int TenantId { get; set; }
    public DateTime LeaseStartDate { get; set; }
    public DateTime LeaseEndDate { get; set; }
    public decimal MonthlyRent { get; set; }
    public decimal SecurityDeposit { get; set; }
    public int PaymentDueDay { get; set; } = 1;
    public string LeaseType { get; set; } = "Fixed";
    public string Status { get; set; } = LeaseStatuses.Active;
    public decimal? LateFeeAmount { get; set; }
    public int? LateFeeGracePeriod { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Item 5 lifecycle: termination / renewal links so audit trail is intact.
    public DateTime? TerminationDate { get; set; }
    public string? TerminationReason { get; set; }
    public string? TerminatedBy { get; set; }
    public bool DepositRefunded { get; set; } = false;
    public decimal? DepositRefundedAmount { get; set; }

    public int? RenewedFromLeaseId { get; set; }     // null on first lease; set when this lease was created by a renewal
    public int? RenewedToLeaseId { get; set; }       // set when a successor lease has been created from this one

    // Lifecycle stamps (LEASE_AUDIT Flaw #16 stop-gap; AuditLog has the full
    // record). Nullable so unrenewed/untransferred leases stay null.
    public string? RenewedBy { get; set; }
    public string? TransferredBy { get; set; }

    // Optimistic concurrency for Renew/Terminate/Transfer (LEASE_AUDIT Flaw #2).
    // Two operators racing on the same lease can't silently overwrite each other.
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }

    // ── Deposit + insurance (separate-invoices flow) ──
    // SecurityDeposit (existing field above) holds the amount required.
    // DepositInvoiced is the idempotency flag flipped when the Pending→Active
    // Activate action creates the dedicated Deposit invoice — re-activating
    // a lease that already has one doesn't double-bill.
    public bool DepositInvoiced { get; set; } = false;

    /// <summary>
    /// Optional insurance premium per billing period. 0 means no insurance
    /// is charged through this lease (tenant may have insurance elsewhere).
    /// </summary>
    public decimal InsuranceAmount { get; set; } = 0m;

    /// <summary>
    /// Cadence — see <see cref="Core.Enums.InsuranceFrequencies"/>.
    /// "None" is the default and disables Insurance invoice generation
    /// regardless of <see cref="InsuranceAmount"/>.
    /// </summary>
    public string InsuranceFrequency { get; set; } = "None";

    /// <summary>
    /// Anchor for annual + one-time billing. Null defaults to LeaseStartDate
    /// at invoice-generation time. Monthly insurance ignores this.
    /// </summary>
    public DateTime? InsuranceStartDate { get; set; }

    // Navigation properties
    public Unit Unit { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

public static class LeaseStatuses
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Terminated = "Terminated";
    public const string Renewed = "Renewed";
}
