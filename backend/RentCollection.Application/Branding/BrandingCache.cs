using Microsoft.Extensions.Options;
using RentCollection.Core.Entities;

namespace RentCollection.Application.Branding;

/// <summary>
/// Process-wide holder of the effective <see cref="BrandingSettings"/>:
/// appsettings.json baseline with the database <see cref="BrandingProfile"/>
/// (Company Settings page) layered on top. The PDF renderer reads
/// <see cref="Current"/> synchronously, so the merge is done once at startup
/// and again whenever the admin saves, not per render.
/// </summary>
public sealed class BrandingCache
{
    private readonly BrandingSettings _baseline;
    private volatile BrandingSettings _current;

    public BrandingCache(IOptions<BrandingSettings> baseline)
    {
        _baseline = baseline.Value;
        _current = baseline.Value;
    }

    /// <summary>Effective settings for the next PDF.</summary>
    public BrandingSettings Current => _current;

    /// <summary>Layer a stored profile (or null = config only) over the baseline.</summary>
    public void Apply(BrandingProfile? profile) => _current = Merge(_baseline, profile);

    public static BrandingSettings Merge(BrandingSettings b, BrandingProfile? p)
    {
        static string? Pick(string? db, string? cfg) => string.IsNullOrWhiteSpace(db) ? cfg : db.Trim();

        if (p == null) return b;
        return new BrandingSettings
        {
            CompanyName = Pick(p.CompanyName, b.CompanyName) ?? b.CompanyName,
            TagLine = Pick(p.TagLine, b.TagLine),
            LogoPath = b.LogoPath,
            PrimaryHex = b.PrimaryHex,
            SecondaryHex = b.SecondaryHex,
            ContactAddress = Pick(p.ContactAddress, b.ContactAddress),
            ContactPhone = Pick(p.ContactPhone, b.ContactPhone),
            ContactEmail = Pick(p.ContactEmail, b.ContactEmail),
            Website = Pick(p.Website, b.Website),
            Bank = new BrandingSettings.BankInfo
            {
                Name = Pick(p.BankName, b.Bank.Name),
                AccountHolder = Pick(p.BankAccountHolder, b.Bank.AccountHolder),
                Iban = Pick(p.BankIban, b.Bank.Iban),
                SwiftBic = Pick(p.BankSwiftBic, b.Bank.SwiftBic),
                KnetReceiver = b.Bank.KnetReceiver,
            },
            FooterTerms = Pick(p.FooterTerms, b.FooterTerms),
            FooterTermsAr = Pick(p.FooterTermsAr, b.FooterTermsAr),
        };
    }
}
