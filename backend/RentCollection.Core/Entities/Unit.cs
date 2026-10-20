namespace RentCollection.Core.Entities;

public class Unit
{
    public int UnitId { get; set; }
    public int PropertyId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public int? SquareFeet { get; set; }
    public int? Bathrooms { get; set; }
    public int? Bedrooms { get; set; }
    public string Status { get; set; } = "Vacant";
    public decimal MonthlyRent { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // ── Classification fields (Unit revamp plan, Phase A) ──
    /// <summary>Floor this unit sits on — free text so non-numeric floors like
    /// "Mezzanine", "Ground" or "Basement" are allowed (null/empty = unknown).</summary>
    public string? FloorNumber { get; set; }

    /// <summary>Derived size band — see <see cref="Core.Enums.UnitSizeBands"/>.
    /// Populated on Create/Edit from Bedrooms + UnitType. Stored so chip
    /// filters can index on it cleanly.</summary>
    public string SizeBand { get; set; } = string.Empty;

    /// <summary>When a Vacant unit becomes available (typically set by Terminate /
    /// Transfer). Null on perpetually-vacant units. Surfaced as "Available from …"
    /// on the unit details page so admins can quote dates to viewing tenants.</summary>
    public DateTime? AvailableFromDate { get; set; }

    /// <summary>Optimistic-concurrency token — mirrors Lease.RowVersion so the
    /// daily reconciliation job and a concurrent admin edit can't silently
    /// overwrite each other.</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }

    // Navigation properties
    public Property Property { get; set; } = null!;
    public ICollection<Lease> Leases { get; set; } = new List<Lease>();
}
