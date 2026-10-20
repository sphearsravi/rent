namespace RentCollection.Core.Entities;

/// <summary>
/// Admin-managed dropdown / reference value. One generic table backs every
/// classification dropdown in the system (property type, governorate, unit type,
/// document type, maintenance category/priority, payment method, legal-case type,
/// …). <see cref="Code"/> is the stable value stored on records — it never
/// changes with language; only the bilingual <see cref="LabelEn"/> /
/// <see cref="LabelAr"/> vary. Status / state-machine values are intentionally
/// NOT stored here (code branches on those exact strings).
/// </summary>
public class LookupItem
{
    public int LookupItemId { get; set; }

    /// <summary>Which dropdown this belongs to, e.g. "PropertyType", "Governorate".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The canonical value persisted on records (English/stable). Unique per category.</summary>
    public string Code { get; set; } = string.Empty;

    public string LabelEn { get; set; } = string.Empty;
    public string LabelAr { get; set; } = string.Empty;

    /// <summary>Display order within the dropdown (ascending).</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Soft on/off. Inactive items are hidden from NEW selections but existing
    /// records that already reference the code still resolve their label.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Null = global (all companies). Reserved for future per-company overrides.</summary>
    public int? CompanyId { get; set; }

    /// <summary>Seeded system values can't be deleted from the UI (only deactivated/relabelled).</summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>Culture-aware label — Arabic when the UI is Arabic, else English (falls back to the other, then Code).</summary>
    public string Label(bool arabic) =>
        arabic
            ? (!string.IsNullOrWhiteSpace(LabelAr) ? LabelAr : (!string.IsNullOrWhiteSpace(LabelEn) ? LabelEn : Code))
            : (!string.IsNullOrWhiteSpace(LabelEn) ? LabelEn : (!string.IsNullOrWhiteSpace(LabelAr) ? LabelAr : Code));
}

/// <summary>The classification dropdown categories managed via the Lookups module.</summary>
public static class LookupCategories
{
    public const string PropertyType = "PropertyType";
    public const string Governorate = "Governorate";
    public const string UnitType = "UnitType";
    public const string UnitSizeBand = "UnitSizeBand";
    public const string DocumentType = "DocumentType";
    public const string MaintenanceCategory = "MaintenanceCategory";
    public const string MaintenancePriority = "MaintenancePriority";
    public const string PaymentMethod = "PaymentMethod";
    public const string LegalCaseType = "LegalCaseType";
    public const string InsuranceFrequency = "InsuranceFrequency";
    public const string LeaseDocumentType = "LeaseDocumentType";
    public const string TenantDocumentType = "TenantDocumentType";

    /// <summary>Display names for the category picker (English). Arabic handled in the view.</summary>
    public static readonly (string Key, string LabelEn, string LabelAr)[] All =
    {
        (PropertyType,        "Property Type",        "نوع العقار"),
        (Governorate,         "Governorate",          "المحافظة"),
        (UnitType,            "Unit Type",            "نوع الوحدة"),
        (UnitSizeBand,        "Unit Size",            "حجم الوحدة"),
        (DocumentType,        "Document Type",        "نوع المستند"),
        (MaintenanceCategory, "Maintenance Category", "فئة الصيانة"),
        (MaintenancePriority, "Maintenance Priority", "أولوية الصيانة"),
        (PaymentMethod,       "Payment Method",       "طريقة الدفع"),
        (LegalCaseType,       "Legal Case Type",      "نوع القضية"),
        (InsuranceFrequency,  "Insurance Frequency",  "دورية التأمين"),
        (LeaseDocumentType,   "Lease Document Type",  "نوع مستند العقد"),
        (TenantDocumentType,  "Tenant Document Type", "نوع مستند المستأجر"),
    };
}
