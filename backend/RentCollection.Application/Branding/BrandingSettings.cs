namespace RentCollection.Application.Branding;

/// <summary>
/// Strongly-typed branding for generated PDFs (invoice, receipt, tenant
/// statement, executive dashboard). Bound from <c>appsettings.json</c> →
/// <c>Branding</c> section so swapping the operator is a config change,
/// not a code edit.
///
/// Single-tenant by design — every PDF rendered by the running app uses
/// these values. Multi-tenant branding (per-Company logos / colours) is
/// captured as a deferred follow-up in the invoice plan.
/// </summary>
public class BrandingSettings
{
    public string CompanyName { get; set; } = "RentCollection";
    public string? TagLine { get; set; }

    /// <summary>
    /// Logo file path, resolved relative to the web root (e.g.
    /// <c>img/branding/logo.png</c>). If missing or the file isn't on disk,
    /// the renderer falls back to a text-only header.
    /// </summary>
    public string? LogoPath { get; set; }

    /// <summary>Hex including the leading <c>#</c>. QuestPDF accepts <c>#RRGGBB</c>.</summary>
    public string PrimaryHex { get; set; } = "#6366F1";

    /// <summary>Hex including the leading <c>#</c>.</summary>
    public string SecondaryHex { get; set; } = "#0F172A";

    public string? ContactAddress { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Website { get; set; }

    public BankInfo Bank { get; set; } = new();

    /// <summary>
    /// Footer terms — line about payment timing, late fees, signed-and-issued
    /// disclaimers. Shown verbatim at the bottom of every invoice / receipt.
    /// </summary>
    public string? FooterTerms { get; set; }

    /// <summary>
    /// Arabic version of <see cref="FooterTerms"/>, used on Arabic-culture
    /// invoices. Falls back to <see cref="FooterTerms"/> when empty.
    /// </summary>
    public string? FooterTermsAr { get; set; }

    public class BankInfo
    {
        public string? Name { get; set; }
        public string? AccountHolder { get; set; }
        public string? Iban { get; set; }
        public string? SwiftBic { get; set; }
        public string? KnetReceiver { get; set; }
    }
}
