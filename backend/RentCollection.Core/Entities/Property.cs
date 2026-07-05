namespace RentCollection.Core.Entities;

public class Property
{
    public int PropertyId { get; set; }
    public int CompanyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string? PACINumber { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Governorate { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Block { get; set; } = string.Empty;
    public string? BuildingNumber { get; set; }
    public string? Street { get; set; }
    public string PropertyType { get; set; } = string.Empty;
    public int NumberOfUnits { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal? PropertyValue { get; set; }
    public string Status { get; set; } = "Active";
    public string? ManagerId { get; set; }
    public string? ImageUrl { get; set; }

    // Payment Configuration
    public bool AcceptKNET { get; set; } = true;
    public bool AcceptCash { get; set; } = true;
    public bool AcceptBankTransfer { get; set; } = true;
    public string? KNETMerchantId { get; set; }
    public string? KNETMerchantKey { get; set; }

    // Building-Specific KNET Account Configuration
    public string? KNETAccountID { get; set; }          // Unique KNET account identifier for this building
    public string? KNETMID { get; set; }                // KNET Merchant ID for this building
    public string? KNETBankName { get; set; }           // Bank name for KNET settlement
    public string? KNETBankAccountNumber { get; set; }  // Bank account for KNET settlement
    public string? KnetTranportalId { get; set; }        // Building-specific KNET merchant ID
    public string? KnetTranportalPwd { get; set; }      // Building-specific KNET merchant password
    public string? KnetTerminalResourceKey { get; set; } // Building-specific KNET encryption key

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Navigation properties
    public Company Company { get; set; } = null!;
    public ApplicationUser? Manager { get; set; }
    public ICollection<Unit> Units { get; set; } = new List<Unit>();
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
