using System.ComponentModel.DataAnnotations;

namespace RentCollection.Core.Entities;

/// <summary>
/// Singleton row holding the company and bank details printed on every
/// generated PDF (invoice "How to pay" block, receipts, statements).
///
/// Lives in the database so the office can change the bank account or the
/// footer wording from the Company Settings page without a redeploy. Any
/// field left empty falls back to the <c>Branding</c> section of
/// appsettings.json (see BrandingCache.Merge). Logo and colours stay in
/// configuration — they are a deploy-time design decision.
/// </summary>
public class BrandingProfile
{
    public int BrandingProfileId { get; set; }

    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(200)] public string? TagLine { get; set; }
    [MaxLength(300)] public string? ContactAddress { get; set; }
    [MaxLength(50)]  public string? ContactPhone { get; set; }
    [MaxLength(200)] public string? ContactEmail { get; set; }
    [MaxLength(200)] public string? Website { get; set; }

    [MaxLength(200)] public string? BankName { get; set; }
    [MaxLength(200)] public string? BankAccountHolder { get; set; }
    [MaxLength(50)]  public string? BankIban { get; set; }
    [MaxLength(20)]  public string? BankSwiftBic { get; set; }

    [MaxLength(1000)] public string? FooterTerms { get; set; }
    [MaxLength(1000)] public string? FooterTermsAr { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
    [MaxLength(100)] public string? ModifiedBy { get; set; }
}
