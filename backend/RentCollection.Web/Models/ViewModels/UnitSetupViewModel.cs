using System.ComponentModel.DataAnnotations;

namespace RentCollection.Web.Models.ViewModels;

/// <summary>
/// Backs the "Unit Setup" bulk grid. When a property is created with
/// <c>NumberOfUnits &gt; 0</c>, that count is treated as an instruction to
/// create that many units — this VM renders one editable row per unit so the
/// whole building is filled in and saved in a single POST, instead of the
/// one-at-a-time Units/Create form.
/// </summary>
public class UnitSetupViewModel
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>Prefix used to auto-number the rows, e.g. "A-" → A-1, A-2 …
    /// Empty prefix gives plain sequential numbers.</summary>
    public string? Prefix { get; set; }

    /// <summary>First sequence number to use — existing unit count + 1, so
    /// auto-generated numbers never collide with units already on the property.</summary>
    public int StartNumber { get; set; } = 1;

    public List<UnitSetupRow> Units { get; set; } = new();
}

public class UnitSetupRow
{
    [Required(ErrorMessage = "Unit number is required")]
    public string UnitNumber { get; set; } = string.Empty;
    public string? FloorNumber { get; set; }
    public string? UnitType { get; set; }
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public int? SquareFeet { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Rent cannot be negative")]
    public decimal MonthlyRent { get; set; }
}
