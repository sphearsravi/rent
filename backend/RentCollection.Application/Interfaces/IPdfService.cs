namespace RentCollection.Application.Interfaces;

/// <summary>
/// Renders financial documents (invoices, receipts) to PDF. Returns raw
/// bytes so the controller can choose to stream, attach to email, or store.
/// </summary>
public interface IPdfService
{
    byte[] RenderInvoice(InvoicePdfModel model);
    byte[] RenderReceipt(ReceiptPdfModel model);

    /// <summary>
    /// Renders the executive dashboard as a print-ready PDF. Used by both the
    /// on-demand "Download PDF" button on /Reports/ExecutiveDashboard and the
    /// scheduled monthly email job (Sprint 4).
    /// </summary>
    byte[] RenderExecutiveDashboard(ExecutiveDashboardPdfModel model);

    /// <summary>
    /// Tenant payment statement — single-page summary: contact card,
    /// active leases, last 12 completed payments, outstanding balance.
    /// </summary>
    byte[] RenderTenantStatement(TenantStatementPdfModel model);
}

public class TenantStatementPdfModel
{
    public DateTime GeneratedAt { get; set; }
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? PACINumber { get; set; }
    public string? CompanyName { get; set; }
    public decimal Outstanding { get; set; }
    public string Currency { get; set; } = "KWD";
    public List<TenantStatementLease> Leases { get; set; } = new();
    public List<TenantStatementPayment> RecentPayments { get; set; } = new();
}

public record TenantStatementLease(
    string Property, string Unit,
    DateTime LeaseStartDate, DateTime LeaseEndDate, decimal MonthlyRent);

public record TenantStatementPayment(
    DateTime PaymentDate, decimal AmountPaid, string Method, string? Reference);

public class ExecutiveDashboardPdfModel
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string? CompanyName { get; set; }
    public decimal Collected { get; set; }
    public decimal Outstanding { get; set; }
    public int OverdueCount { get; set; }
    public decimal OccupancyPercent { get; set; }
    public int TotalUnits { get; set; }
    public int OccupiedUnits { get; set; }
    public decimal CollectedDelta { get; set; }
    /// <summary>(month label, collected) for the last 12 months.</summary>
    public List<(string Label, decimal Value)> Trend { get; set; } = new();
    /// <summary>(status, count) — Paid / Pending / Overdue / Partially Paid / Cancelled etc.</summary>
    public List<(string Status, int Count)> StatusBreakdown { get; set; } = new();
    /// <summary>(property name, collected) — top 5 in range.</summary>
    public List<(string Name, decimal Value)> TopProperties { get; set; } = new();
    public string Currency { get; set; } = "KWD";
}

public class InvoicePdfModel
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string Currency { get; set; } = "KWD";
    public string? Notes { get; set; }
    public List<InvoicePdfLine> Lines { get; set; } = new();

    /// <summary>
    /// Billing-period label shown in the header meta block (e.g. "June 2026").
    /// Independent of <see cref="InvoiceDate"/> so a December-issued invoice
    /// for January rent reads correctly. Null/empty hides the row.
    /// </summary>
    public string? BillingPeriod { get; set; }

    /// <summary>Tenant's individual PACI number — shown under BILL TO.</summary>
    public string? TenantPaciNumber { get; set; }

    /// <summary>Property/building PACI number — shown under PROPERTY.</summary>
    public string? PropertyPaciNumber { get; set; }

    /// <summary>
    /// When (most recently) this invoice received a payment. Surfaced as
    /// "Paid on …" in the totals block; hidden when <see cref="PaidAmount"/>
    /// is zero or the date isn't known.
    /// </summary>
    public DateTime? PaidDate { get; set; }

    // ── Address fields rendered under the UNIT column on the invoice. ──
    // Each is independent so the renderer can compose only what's present
    // (no "Building , Street " holes when a property has partial data).

    /// <summary>Optional floor designation (e.g. "1", "Ground", "Mezzanine").</summary>
    public string? UnitFloor { get; set; }

    /// <summary>Property building number (from Property.BuildingNumber).</summary>
    public string? PropertyBuildingNumber { get; set; }

    /// <summary>Property street name/number (from Property.Street).</summary>
    public string? PropertyStreet { get; set; }

    /// <summary>Property block (from Property.Block) — Kuwait addressing unit.</summary>
    public string? PropertyBlock { get; set; }

    /// <summary>Property area / district (from Property.Area).</summary>
    public string? PropertyArea { get; set; }

    /// <summary>Property governorate (from Property.Governorate).</summary>
    public string? PropertyGovernorate { get; set; }

    /// <summary>
    /// Invoice category — "Rent" (default), "Deposit", "Insurance", "LateFee",
    /// "MaintenanceFee", "Other". Drives the PDF header title so a deposit
    /// invoice reads "SECURITY DEPOSIT INVOICE" instead of just "INVOICE".
    /// </summary>
    public string InvoiceType { get; set; } = "Rent";

    /// <summary>
    /// Render culture — "en" (default) or "ar". When Arabic, the PDF is produced
    /// right-to-left with Arabic labels and an Arabic-capable font. Numbers/amounts
    /// stay in Western digits, matching the app's i18n rule.
    /// </summary>
    public string Culture { get; set; } = "en";
    public bool IsArabic => Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;

    // ── 2026-10 redesign additions ──

    /// <summary>Tenant phone — shown under BILL TO when present.</summary>
    public string? TenantPhone { get; set; }

    /// <summary>Tenant email — shown under BILL TO when present.</summary>
    public string? TenantEmail { get; set; }

    /// <summary>Lease term, shown under PROPERTY &amp; UNIT when both are known.</summary>
    public DateTime? LeaseStartDate { get; set; }
    public DateTime? LeaseEndDate { get; set; }

    /// <summary>
    /// Completed payments already applied to this invoice — rendered as a
    /// "Payments received" table so a partially paid invoice explains itself.
    /// </summary>
    public List<InvoicePdfPayment> Payments { get; set; } = new();

    /// <summary>
    /// When the document was produced. Drives the "Overdue by N days" /
    /// "Due in N days" callout and the footer timestamp. Defaults to now.
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class InvoicePdfLine
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>One completed payment applied to an invoice (invoice PDF).</summary>
public record InvoicePdfPayment(DateTime PaymentDate, string Method, string? Reference, decimal Amount);

public class ReceiptPdfModel
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = "KWD";
    public string? TransactionReference { get; set; }
    public string? InvoiceNumber { get; set; }
    /// <summary>Hand-written receipt-book number for cash (printed when present).</summary>
    public string? ManualReceiptNumber { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Balance still owed on the invoice after this payment (null when the
    /// payment isn't tied to an invoice). 0 renders as "Invoice settled".
    /// </summary>
    public decimal? InvoiceBalanceAfter { get; set; }

    /// <summary>"en" (default) or "ar" — same rule as the invoice.</summary>
    public string Culture { get; set; } = "en";
    public bool IsArabic => Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
