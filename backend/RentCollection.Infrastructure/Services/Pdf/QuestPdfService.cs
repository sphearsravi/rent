using System.Globalization;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RentCollection.Application.Branding;
using RentCollection.Application.Interfaces;

namespace RentCollection.Infrastructure.Services.Pdf;

/// <summary>
/// QuestPDF-based invoice/receipt/statement/dashboard renderer. Licensed
/// Community in Program.cs (free under $1M USD revenue).
///
/// Invoice and receipt share one visual system (2026-10 redesign):
///   • dark brand band header with the logo, company block, document title,
///     number and a status pill — the square gold-on-black logo sits in it
///     natively instead of floating as a black square on white;
///   • a four-tile meta strip (issued / due / period / amount due) so the
///     numbers a tenant or accountant looks for are readable at a glance;
///   • BILL TO and PROPERTY &amp; UNIT cards, a numbered line-item table,
///     totals with a due-status callout, a "payments received" table when
///     anything has been applied, and a "how to pay" strip (app / bank / cash);
///   • status-aware overlays: PAID watermark, OVERDUE top bar;
///   • full Arabic (RTL, Cairo font, Western digits) when <c>Culture</c> = ar.
///
/// <see cref="RenderInvoicePreviewPng"/> / <see cref="RenderReceiptPreviewPng"/>
/// rasterise the same document so designs can be reviewed as images.
/// </summary>
public class QuestPdfService : IPdfService
{
    private readonly BrandingSettings _brand;

    // Register the embedded Cairo font (Google Fonts, OFL — full Latin + Arabic)
    // with QuestPDF exactly once, even under concurrent RenderInvoice calls.
    // FontManager.RegisterFont(Stream) is the confirmed API in QuestPDF 2024.10.3.
    private const string DocFont = "Cairo";
    private const string MonoFont = "Courier New";
    private static readonly object _fontLock = new();
    private static bool _fontRegistered;

    // Neutral palette shared by every document. Brand colours come from settings.
    private const string Muted = "#64748b";
    private const string Faint = "#94a3b8";
    private const string Line = "#e2e8f0";
    private const string Soft = "#f8fafc";
    private const string OnDarkMuted = "#cbd5e1";
    private const string Green = "#10b981";
    private const string Red = "#dc2626";
    private const string Amber = "#d97706";
    private const string Blue = "#2563eb";
    private const string Grey = "#64748b";

    private static void EnsureDocFontRegistered()
    {
        if (_fontRegistered) return;
        lock (_fontLock)
        {
            if (_fontRegistered) return;
            using var s = typeof(QuestPdfService).Assembly.GetManifestResourceStream(
                "RentCollection.Infrastructure.Services.Pdf.Fonts.Cairo.ttf");
            if (s != null)
                QuestPDF.Drawing.FontManager.RegisterFont(s);
            _fontRegistered = true;
        }
    }

    public QuestPdfService(IOptions<BrandingSettings> brand)
    {
        _brand = brand.Value;
    }

    // Preferred DI path: the cache layers the DB-stored Company Settings
    // (bank account, contact lines, footer) over appsettings.json.
    public QuestPdfService(BrandingCache cache)
    {
        _brand = cache.Current;
    }

    // Parameterless ctor for the legacy DI registration path. Falls back to
    // a default brand so PDF generation never breaks if branding is unset.
    public QuestPdfService() : this(Options.Create(new BrandingSettings())) { }

    // Money is ALWAYS formatted with InvariantCulture so the decimal point is a
    // period and the 3 fils digits are consistent. Without this, rendering under
    // an Arabic culture used a comma as the decimal separator, so "13.750" (13 KWD
    // 750 fils) printed as "13,750" and read like thirteen thousand.
    private static string Amt(decimal d) =>
        d.ToString("#,##0.000", CultureInfo.InvariantCulture);

    private static readonly CultureInfo ArKw = CultureInfo.GetCultureInfo("ar-KW");

    // QuestPDF parses 8-digit hex as #AARRGGBB (alpha FIRST). "#dc262614" is
    // therefore an almost-opaque dark olive, not a 8% red tint.
    private static string Alpha(string hex, string aa) => "#" + aa + hex.TrimStart('#');

    // Document-wide line height for Cairo. It was 0.85, which made each line
    // shorter than its own glyphs: titles hit the number below, labels hit
    // their values, Arabic marks ran into the next line. 1.2 clears them and
    // a normal invoice still fits on one page.
    private const float LineHeight = 1.2f;

    // In an RTL document a Latin run that ends in punctuation ("W.L.L.",
    // "ravi@example.com") gets its trailing mark pulled to the visual start by
    // the bidi algorithm. A left-to-right mark after the run pins it.
    private static string Ltr(string? s, bool ar) =>
        string.IsNullOrEmpty(s) ? s ?? string.Empty : ar ? s + "‎" : s;

    /// <summary>
    /// "Pending" past its due date is overdue for the reader even if the nightly
    /// job hasn't flipped the row yet — pill, top bar and callout must agree.
    /// </summary>
    private static string EffectiveStatus(InvoicePdfModel m) =>
        Is(m.Status, "Pending") && m.DueDate.Date < m.GeneratedAt.Date ? "Overdue" : m.Status;

    // Dates: English "05 Jun 2026"; Arabic month name with WESTERN digits
    // (app i18n rule — never Arabic-Indic digits in numbers).
    private static string D(DateTime d, bool ar) =>
        ar ? $"{d.Day:00} {d.ToString("MMMM", ArKw)} {d.Year}"
           : d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static string Dt(DateTime d, bool ar) =>
        $"{D(d, ar)} {d:HH:mm}";

    private bool HasLogo =>
        !string.IsNullOrWhiteSpace(_brand.LogoPath) && File.Exists(_brand.LogoPath);

    // ═══════════════════════════════════════════════════════════ INVOICE ═══

    public byte[] RenderInvoice(InvoicePdfModel m)
    {
        EnsureDocFontRegistered();
        return BuildInvoice(m).GeneratePdf();
    }

    /// <summary>PNG per page of the exact invoice document (design review / previews).</summary>
    public IReadOnlyList<byte[]> RenderInvoicePreviewPng(InvoicePdfModel m, int dpi = 110)
    {
        EnsureDocFontRegistered();
        return BuildInvoice(m)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi })
            .ToList();
    }

    private IDocument BuildInvoice(InvoicePdfModel m) => Document.Create(container =>
    {
        var ar = m.IsArabic;
        string L(string en, string arText) => ar ? arText : en;
        var status = EffectiveStatus(m);

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(34);
            page.MarginTop(26);
            page.MarginBottom(24);
            page.DefaultTextStyle(s =>
            {
                var st = s.FontSize(9.5f).FontColor(_brand.SecondaryHex).FontFamily(DocFont).LineHeight(LineHeight);
                return ar ? st.DirectionFromRightToLeft() : st;
            });

            // OVERDUE: a red bar across the very top of every page so the
            // signal is unmistakable from across a desk.
            if (Is(status, "Overdue"))
            {
                page.Background().Column(c =>
                {
                    c.Item().Height(6).Background(Red);
                    c.Item().Element(_ => { });
                });
            }

            var title = ar
                ? m.InvoiceType switch
                {
                    "Deposit"        => "فاتورة مبلغ التأمين",
                    "Insurance"      => "فاتورة التأمين",
                    "LateFee"        => "فاتورة غرامة تأخير",
                    "MaintenanceFee" => "فاتورة صيانة",
                    "Other"          => "فاتورة",
                    _                => "فاتورة إيجار",
                }
                : m.InvoiceType switch
                {
                    "Deposit"        => "SECURITY DEPOSIT INVOICE",
                    "Insurance"      => "INSURANCE INVOICE",
                    "LateFee"        => "LATE FEE INVOICE",
                    "MaintenanceFee" => "MAINTENANCE INVOICE",
                    "Other"          => "INVOICE",
                    _                => "RENT INVOICE",
                };

            var header = page.Header();
            if (ar) header = header.ContentFromRightToLeft();
            header.Element(h => BrandBand(h, title, m.InvoiceNumber, status, ar));

            var content = page.Content();
            if (ar) content = content.ContentFromRightToLeft();
            content.Element(c => c.Layers(layers =>
            {
                if (Is(status, "Paid"))
                {
                    layers.Layer().AlignCenter().AlignMiddle().Element(w =>
                        w.Rotate(-22).Text(L("PAID", "مدفوعة")).FontSize(150)
                         .Bold().FontColor(Alpha(Green, "22")));
                }
                layers.PrimaryLayer().Element(body => InvoiceBody(body, m));
            }));

            var footer = page.Footer();
            if (ar) footer = footer.ContentFromRightToLeft();
            footer.Element(f => DocFooter(f, ar, m.GeneratedAt));
        });
    });

    private void InvoiceBody(IContainer container, InvoicePdfModel m)
    {
        var ar = m.IsArabic;
        string L(string en, string arText) => ar ? arText : en;
        var today = m.GeneratedAt.Date;
        var status = EffectiveStatus(m);

        container.PaddingTop(12).Column(col =>
        {
            col.Spacing(11);

            // ── Meta strip: issued · due · period · amount due ──
            col.Item().Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem().Element(c => MetaTile(c, L("ISSUED", "تاريخ الإصدار"), D(m.InvoiceDate, ar)));
                var dueColor = Is(status, "Overdue") ? Red : Is(status, "Paid") ? _brand.SecondaryHex : Amber;
                row.RelativeItem().Element(c => MetaTile(c, L("DUE DATE", "تاريخ الاستحقاق"), D(m.DueDate, ar), dueColor));
                row.RelativeItem().Element(c => MetaTile(c, L("BILLING PERIOD", "فترة الفوترة"),
                    string.IsNullOrWhiteSpace(m.BillingPeriod) ? "—" : m.BillingPeriod!));
                row.RelativeItem(1.25f).Background(_brand.SecondaryHex).Padding(9).Column(c =>
                {
                    c.Item().Text(L("AMOUNT DUE", "المبلغ المستحق")).FontColor(_brand.PrimaryHex)
                        .FontSize(7.5f).Bold().LetterSpacing(0.08f);
                    c.Item().PaddingTop(1).Text($"{Amt(m.BalanceAmount)} {m.Currency}")
                        .FontColor("#ffffff").Bold().FontSize(14);
                });
            });

            // ── Due-status callout ──
            var (calloutText, calloutColor) = DueCallout(m, status, today);
            if (calloutText != null)
            {
                col.Item().Background(Alpha(calloutColor, "14")).BorderLeft(3).BorderColor(calloutColor)
                    .PaddingVertical(5).PaddingHorizontal(10)
                    .Text(calloutText).FontColor(calloutColor).SemiBold().FontSize(9.5f);
            }

            // ── Parties: BILL TO · PROPERTY & UNIT ──
            col.Item().Row(row =>
            {
                row.Spacing(10);
                row.RelativeItem().Element(c => Card(c, L("BILL TO", "فاتورة إلى"), card =>
                {
                    card.Item().Text(Ltr(m.TenantName, ar)).Bold().FontSize(12);
                    if (!string.IsNullOrEmpty(m.CompanyName))
                        card.Item().Text(Ltr(m.CompanyName, ar)).FontColor(Muted);
                    if (!string.IsNullOrEmpty(m.TenantPaciNumber))
                        card.Item().PaddingTop(2).Text(t =>
                        {
                            t.Span(L("Civil / PACI ID: ", "الرقم المدني: ")).FontColor(Faint).FontSize(8.5f);
                            t.Span(Ltr(m.TenantPaciNumber, ar)).FontFamily(MonoFont).FontSize(9);
                        });
                    if (!string.IsNullOrEmpty(m.TenantPhone))
                        card.Item().Text(t =>
                        {
                            t.Span(L("Phone: ", "الهاتف: ")).FontColor(Faint).FontSize(8.5f);
                            t.Span(Ltr(m.TenantPhone, ar)).FontSize(9);
                        });
                    if (!string.IsNullOrEmpty(m.TenantEmail))
                        card.Item().Text(t =>
                        {
                            t.Span(L("Email: ", "البريد: ")).FontColor(Faint).FontSize(8.5f);
                            t.Span(Ltr(m.TenantEmail, ar)).FontSize(9);
                        });
                }));

                row.RelativeItem().Element(c => Card(c, L("PROPERTY & UNIT", "العقار والوحدة"), card =>
                {
                    card.Item().Text(string.IsNullOrEmpty(m.PropertyName) ? "—" : Ltr(m.PropertyName, ar)).Bold().FontSize(12);

                    var unitBits = new List<string>();
                    if (!string.IsNullOrEmpty(m.UnitNumber)) unitBits.Add(L($"Flat {m.UnitNumber}", $"شقة {m.UnitNumber}"));
                    if (!string.IsNullOrEmpty(m.UnitFloor)) unitBits.Add(L($"Floor {m.UnitFloor}", $"الطابق {m.UnitFloor}"));
                    if (unitBits.Count > 0)
                        card.Item().Text(string.Join("  ·  ", unitBits)).SemiBold().FontSize(10);

                    var addr1 = JoinPresent(", ",
                        !string.IsNullOrEmpty(m.PropertyBuildingNumber) ? L($"Building {m.PropertyBuildingNumber}", $"مبنى {m.PropertyBuildingNumber}") : null,
                        !string.IsNullOrEmpty(m.PropertyStreet) ? L($"Street {m.PropertyStreet}", $"شارع {m.PropertyStreet}") : null,
                        !string.IsNullOrEmpty(m.PropertyBlock) ? L($"Block {m.PropertyBlock}", $"قطعة {m.PropertyBlock}") : null);
                    var addr2 = JoinPresent(", ", m.PropertyArea, m.PropertyGovernorate);
                    if (addr1 != null) card.Item().Text(addr1).FontColor(Muted).FontSize(9);
                    if (addr2 != null) card.Item().Text(addr2).FontColor(Muted).FontSize(9);

                    if (!string.IsNullOrEmpty(m.PropertyPaciNumber))
                        card.Item().PaddingTop(2).Text(t =>
                        {
                            t.Span(L("Property PACI: ", "رقم العقار: ")).FontColor(Faint).FontSize(8.5f);
                            t.Span(m.PropertyPaciNumber!).FontFamily(MonoFont).FontSize(9);
                        });
                    if (m.LeaseStartDate.HasValue && m.LeaseEndDate.HasValue)
                        card.Item().Text(t =>
                        {
                            t.Span(L("Lease term: ", "مدة العقد: ")).FontColor(Faint).FontSize(8.5f);
                            t.Span($"{D(m.LeaseStartDate.Value, ar)} – {D(m.LeaseEndDate.Value, ar)}").FontSize(9);
                        });
                }));
            });

            // ── Line items ──
            var rows = m.Lines.Count == 0
                ? new List<InvoicePdfLine> { new() { Description = L("Monthly rent", "الإيجار الشهري"), Amount = m.TotalAmount } }
                : m.Lines;

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(34);  // # (fits 3 digits)
                    c.RelativeColumn(6);   // description
                    c.RelativeColumn(2);   // amount
                });
                table.Header(h =>
                {
                    IContainer HCell(IContainer c) => c.Background(Soft).BorderBottom(1.5f).BorderColor(_brand.PrimaryHex)
                        .PaddingVertical(6).PaddingHorizontal(8);
                    HCell(h.Cell()).Text("#").FontColor(Muted).FontSize(8).Bold();
                    HCell(h.Cell()).Text(L("DESCRIPTION", "الوصف")).FontColor(Muted).FontSize(8).Bold().LetterSpacing(0.06f);
                    HCell(h.Cell()).AlignRight().Text(L($"AMOUNT ({m.Currency})", $"المبلغ ({m.Currency})")).FontColor(Muted).FontSize(8).Bold().LetterSpacing(0.06f);
                });
                var i = 0;
                foreach (var line in rows)
                {
                    i++;
                    IContainer Cell(IContainer c) => c.BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(8);
                    Cell(table.Cell()).Text(i.ToString()).FontColor(Faint).FontSize(9);
                    Cell(table.Cell()).Text(line.Description);
                    Cell(table.Cell()).AlignRight().Text(Amt(line.Amount)).SemiBold();
                }
            });

            // ── Totals (right) ──
            col.Item().Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(250).Column(c =>
                {
                    void TotalRow(string label, string value, string? color = null, bool bold = false)
                    {
                        c.Item().PaddingVertical(2).Row(r =>
                        {
                            r.RelativeItem().Text(label).FontColor(Muted);
                            var v = r.RelativeItem().AlignRight().Text(value);
                            if (color != null) v.FontColor(color);
                            if (bold) v.SemiBold();
                        });
                    }
                    TotalRow(L("Subtotal", "المجموع"), $"{Amt(m.TotalAmount)} {m.Currency}");
                    TotalRow(L("Paid", "المدفوع"), $"− {Amt(m.PaidAmount)} {m.Currency}", m.PaidAmount > 0 ? Green : Muted);
                    if (m.PaidAmount > 0 && m.PaidDate.HasValue)
                        TotalRow(L("Last payment", "آخر دفعة"), D(m.PaidDate.Value, ar), Green);

                    c.Item().PaddingTop(4).Background(_brand.SecondaryHex).PaddingVertical(8).PaddingHorizontal(10).Row(r =>
                    {
                        r.RelativeItem().AlignMiddle().Text(L("BALANCE DUE", "الرصيد المستحق"))
                            .FontColor(_brand.PrimaryHex).Bold().FontSize(9.5f).LetterSpacing(0.08f);
                        r.RelativeItem().AlignRight().Text($"{Amt(m.BalanceAmount)} {m.Currency}")
                            .FontColor("#ffffff").Bold().FontSize(14);
                    });
                });
            });

            // ── Payments received ──
            if (m.Payments.Count > 0)
            {
                col.Item().PaddingTop(2).Column(c =>
                {
                    c.Item().Element(x => SectionTitle(x, L("PAYMENTS RECEIVED", "الدفعات المستلمة")));
                    c.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(3); cd.RelativeColumn(2);
                        });
                        table.Header(h =>
                        {
                            IContainer HCell(IContainer x) => x.Background(Soft).BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(8);
                            HCell(h.Cell()).Text(L("DATE", "التاريخ")).FontColor(Muted).FontSize(8).Bold();
                            HCell(h.Cell()).Text(L("METHOD", "الطريقة")).FontColor(Muted).FontSize(8).Bold();
                            HCell(h.Cell()).Text(L("REFERENCE", "المرجع")).FontColor(Muted).FontSize(8).Bold();
                            HCell(h.Cell()).AlignRight().Text(L($"AMOUNT ({m.Currency})", $"المبلغ ({m.Currency})")).FontColor(Muted).FontSize(8).Bold();
                        });
                        foreach (var p in m.Payments.OrderBy(p => p.PaymentDate))
                        {
                            IContainer Cell(IContainer x) => x.BorderBottom(1).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(8);
                            Cell(table.Cell()).Text(D(p.PaymentDate, ar)).FontSize(9);
                            Cell(table.Cell()).Text(p.Method).FontSize(9);
                            Cell(table.Cell()).Text(string.IsNullOrEmpty(p.Reference) ? "—" : Ltr(p.Reference, ar)).FontFamily(MonoFont).FontSize(8.5f).FontColor(Muted);
                            Cell(table.Cell()).AlignRight().Text(Amt(p.Amount)).FontColor(Green).SemiBold().FontSize(9);
                        }
                    });
                });
            }

            // ── How to pay (only while something is owed) ──
            if (m.BalanceAmount > 0 && !Is(status, "Cancelled"))
            {
                col.Item().Column(c =>
                {
                    c.Item().Element(x => SectionTitle(x, L("HOW TO PAY", "طرق الدفع")));
                    c.Item().Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem().Element(x => PayCard(x,
                            L("K-NET — Alrtam app", "كي-نت — تطبيق الرتم"),
                            L("Open the invoice in the Alrtam tenant app and tap Pay now. Instant confirmation.",
                              "افتح الفاتورة في تطبيق الرتم واضغط «ادفع الآن». تأكيد فوري.")));

                        var bankLines = new List<(string, string)>();
                        if (!string.IsNullOrEmpty(_brand.Bank.Name)) bankLines.Add((L("Bank", "البنك"), Ltr(_brand.Bank.Name, ar)));
                        if (!string.IsNullOrEmpty(_brand.Bank.AccountHolder)) bankLines.Add((L("Account", "الحساب"), Ltr(_brand.Bank.AccountHolder, ar)));
                        if (!string.IsNullOrEmpty(_brand.Bank.Iban)) bankLines.Add((L("IBAN", "الآيبان"), Ltr(_brand.Bank.Iban, ar)));
                        if (!string.IsNullOrEmpty(_brand.Bank.SwiftBic)) bankLines.Add((L("SWIFT", "سويفت"), Ltr(_brand.Bank.SwiftBic, ar)));
                        row.RelativeItem().Element(x => PayCard(x,
                            L("Bank transfer", "تحويل بنكي"),
                            bankLines.Count == 0
                                ? L("Transfer to the company account (details from the office), then upload the proof in the app.",
                                    "حوّل إلى حساب الشركة (التفاصيل من المكتب) ثم ارفع إثبات التحويل في التطبيق.")
                                : L("Then upload the transfer proof in the app.", "ثم ارفع إثبات التحويل في التطبيق."),
                            bankLines));

                        row.RelativeItem().Element(x => PayCard(x,
                            L("Cash at the office", "نقداً في المكتب"),
                            L("Request it in the app, pay at the counter; the receipt is issued once staff confirm.",
                              "اطلب الدفع النقدي من التطبيق وادفع لدى الكاونتر؛ يصدر الإيصال بعد تأكيد الموظفين.")));
                    });
                    c.Item().PaddingTop(5).Text(t =>
                    {
                        t.Span(L("Reference for every payment: ", "المرجع لكل دفعة: ")).FontColor(Muted).FontSize(8.5f);
                        t.Span(m.InvoiceNumber).SemiBold().FontSize(9);
                    });
                });
            }

            // ── Notes ──
            if (!string.IsNullOrWhiteSpace(m.Notes))
            {
                col.Item().Background(Soft).Border(1).BorderColor(Line).Padding(9).Column(c =>
                {
                    c.Item().Text(L("NOTES", "ملاحظات")).FontColor(Muted).FontSize(7.5f).Bold().LetterSpacing(0.08f);
                    c.Item().PaddingTop(2).Text(Ltr(m.Notes, ar)).FontSize(9).FontColor(Muted);
                });
            }
        });
    }

    /// <summary>One-line verdict under the meta strip, coloured by urgency.</summary>
    private static (string? text, string color) DueCallout(InvoicePdfModel m, string status, DateTime today)
    {
        var ar = m.IsArabic;
        string L(string en, string arText) => ar ? arText : en;
        var days = (m.DueDate.Date - today).Days;

        if (Is(status, "Paid"))
            return (m.PaidDate.HasValue
                ? L($"Paid in full on {D(m.PaidDate.Value, ar)}. Thank you.", $"مدفوعة بالكامل بتاريخ {D(m.PaidDate.Value, ar)}. شكراً لك.")
                : L("Paid in full. Thank you.", "مدفوعة بالكامل. شكراً لك."), Green);
        if (Is(status, "Cancelled"))
            return (L("This invoice has been cancelled and is not payable.", "هذه الفاتورة ملغاة وغير مستحقة الدفع."), Grey);
        if (Is(status, "Pending Verification"))
            return (L("A payment has been submitted and is awaiting office confirmation.", "تم إرسال دفعة وهي بانتظار تأكيد المكتب."), Blue);
        if (Is(status, "Overdue"))
        {
            var late = Math.Max(0, -days);
            return (late == 0
                ? L($"Overdue — please settle {Amt(m.BalanceAmount)} {m.Currency} now.", $"متأخرة — يرجى سداد {Amt(m.BalanceAmount)} {m.Currency} الآن.")
                : L($"Overdue by {late} day{(late == 1 ? "" : "s")} — please settle {Amt(m.BalanceAmount)} {m.Currency} now.",
                    $"متأخرة {late} يوم — يرجى سداد {Amt(m.BalanceAmount)} {m.Currency} الآن."), Red);
        }
        if (Is(status, "Partially Paid"))
            return (L($"Partially paid — {Amt(m.BalanceAmount)} {m.Currency} remains due by {D(m.DueDate, ar)}.",
                      $"مدفوعة جزئياً — المتبقي {Amt(m.BalanceAmount)} {m.Currency} يستحق بتاريخ {D(m.DueDate, ar)}."), Amber);
        if (days == 0)
            return (L("Due today.", "تستحق اليوم."), Amber);
        return (L($"Due in {days} day{(days == 1 ? "" : "s")} — {D(m.DueDate, ar)}.", $"تستحق خلال {days} يوم — {D(m.DueDate, ar)}."), Amber);
    }

    // ═══════════════════════════════════════════════════════════ RECEIPT ═══

    public byte[] RenderReceipt(ReceiptPdfModel m)
    {
        EnsureDocFontRegistered();
        return BuildReceipt(m).GeneratePdf();
    }

    public IReadOnlyList<byte[]> RenderReceiptPreviewPng(ReceiptPdfModel m, int dpi = 110)
    {
        EnsureDocFontRegistered();
        return BuildReceipt(m)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi })
            .ToList();
    }

    private IDocument BuildReceipt(ReceiptPdfModel m) => Document.Create(container =>
    {
        var ar = m.IsArabic;
        string L(string en, string arText) => ar ? arText : en;

        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.MarginHorizontal(26);
            page.MarginTop(22);
            page.MarginBottom(20);
            page.DefaultTextStyle(s =>
            {
                var st = s.FontSize(9.5f).FontColor(_brand.SecondaryHex).FontFamily(DocFont).LineHeight(LineHeight);
                return ar ? st.DirectionFromRightToLeft() : st;
            });

            var header = page.Header();
            if (ar) header = header.ContentFromRightToLeft();
            header.Element(h => BrandBand(h, L("RECEIPT", "إيصال استلام"), m.ReceiptNumber, "Paid", ar, compact: true));

            var content = page.Content();
            if (ar) content = content.ContentFromRightToLeft();
            content.Element(c => c.Layers(layers =>
            {
                // Receipts are always paid by definition — diagonal stamp on every one.
                layers.Layer().AlignCenter().AlignMiddle().Element(w =>
                    w.Rotate(-22).Text(L("PAID", "مدفوعة")).FontSize(110).Bold().FontColor(Alpha(Green, "22")));

                layers.PrimaryLayer().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Element(c => Card(c, L("RECEIVED FROM", "استلمنا من"), card =>
                    {
                        card.Item().Text(Ltr(m.TenantName, ar)).Bold().FontSize(12);
                        card.Item().Text(L($"{m.PropertyName}  ·  Flat {m.UnitNumber}", $"{Ltr(m.PropertyName, ar)}  ·  شقة {m.UnitNumber}")).FontColor(Muted);
                    }));

                    col.Item().Background(_brand.SecondaryHex).Padding(14).Row(r =>
                    {
                        r.RelativeItem().AlignMiddle().Column(c =>
                        {
                            c.Item().Text(L("AMOUNT RECEIVED", "المبلغ المستلم")).FontColor(_brand.PrimaryHex)
                                .FontSize(8).Bold().LetterSpacing(0.08f);
                            c.Item().Text(L("with thanks", "مع الشكر")).FontColor(OnDarkMuted).FontSize(8);
                        });
                        r.RelativeItem().AlignRight().AlignMiddle()
                            .Text($"{Amt(m.AmountPaid)} {m.Currency}").FontColor("#ffffff").Bold().FontSize(22);
                    });

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); });
                        void Row(string label, string value, bool mono = false, string? color = null)
                        {
                            t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(5).Text(label).FontColor(Muted).FontSize(9);
                            var v = t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(5).Text(value).SemiBold().FontSize(9.5f);
                            if (mono) v.FontFamily(MonoFont);
                            if (color != null) v.FontColor(color);
                        }
                        Row(L("Payment date", "تاريخ الدفع"), Dt(m.PaymentDate, ar));
                        Row(L("Method", "طريقة الدفع"), MethodLabel(m.PaymentMethod, ar));
                        if (!string.IsNullOrEmpty(m.TransactionReference))
                            Row(L("Transaction ref.", "مرجع العملية"), Ltr(m.TransactionReference, ar), mono: true);
                        if (!string.IsNullOrEmpty(m.ManualReceiptNumber))
                            Row(L("Manual receipt no.", "رقم الإيصال اليدوي"), Ltr(m.ManualReceiptNumber, ar), mono: true);
                        if (!string.IsNullOrEmpty(m.InvoiceNumber))
                            Row(L("Applied to invoice", "مقابل الفاتورة"), Ltr(m.InvoiceNumber, ar));
                        if (m.InvoiceBalanceAfter.HasValue)
                        {
                            var settled = m.InvoiceBalanceAfter.Value <= 0;
                            Row(L("Invoice balance after", "رصيد الفاتورة بعد الدفع"),
                                settled ? L("Settled — nothing outstanding", "مسددة — لا يوجد مستحقات")
                                        : $"{Amt(m.InvoiceBalanceAfter.Value)} {m.Currency}",
                                color: settled ? Green : Amber);
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(m.Notes))
                        col.Item().Text(Ltr(m.Notes, ar)).Italic().FontColor(Muted).FontSize(8.5f);

                    col.Item().PaddingTop(4).Text(L(
                        "This receipt confirms the payment above. Keep it for your records.",
                        "يؤكد هذا الإيصال استلام الدفعة أعلاه. يرجى الاحتفاظ به."))
                        .FontColor(Faint).FontSize(8);
                });
            }));

            var footer = page.Footer();
            if (ar) footer = footer.ContentFromRightToLeft();
            // Receipts: no invoice payment terms, compact one-line footer.
            footer.Element(f => DocFooter(f, ar, m.GeneratedAt, showTerms: false, compact: true));
        });
    });

    private static string MethodLabel(string method, bool ar) => method switch
    {
        "KNET" or "K-NET" or "Knet" => "K-NET",
        "BankTransfer" => ar ? "تحويل بنكي" : "Bank transfer",
        "Cash"         => ar ? "نقداً" : "Cash",
        "Cheque"       => ar ? "شيك" : "Cheque",
        _              => method,
    };

    // ═════════════════════════════════════════════════ SHARED COMPONENTS ═══

    /// <summary>
    /// Dark brand band: logo · company block · title / number / status pill,
    /// finished with a gold rule. Shared by invoice and receipt.
    /// </summary>
    private void BrandBand(IContainer container, string title, string number, string? status, bool ar, bool compact = false)
    {
        string L(string en, string arText) => ar ? arText : en;
        var logoSize = compact ? 46f : 58f;

        container.Column(col =>
        {
            col.Item().Background(_brand.SecondaryHex).Padding(compact ? 12 : 16).Row(row =>
            {
                row.ConstantItem(logoSize).Height(logoSize).Element(c =>
                {
                    if (HasLogo) c.Image(_brand.LogoPath!).FitArea();
                });
                row.ConstantItem(12);
                row.RelativeItem().AlignMiddle().Column(c =>
                {
                    c.Item().Text(_brand.CompanyName).FontColor(_brand.PrimaryHex).Bold().FontSize(compact ? 13 : 15);
                    if (!string.IsNullOrEmpty(_brand.TagLine))
                        c.Item().Text(_brand.TagLine!.ToUpperInvariant()).FontColor(_brand.PrimaryHex)
                            .FontSize(7).LetterSpacing(0.14f);
                    var bits = new List<string>();
                    if (!string.IsNullOrEmpty(_brand.ContactAddress)) bits.Add(_brand.ContactAddress!);
                    if (!string.IsNullOrEmpty(_brand.ContactPhone)) bits.Add(_brand.ContactPhone!);
                    if (!string.IsNullOrEmpty(_brand.ContactEmail)) bits.Add(_brand.ContactEmail!);
                    if (!string.IsNullOrEmpty(_brand.Website)) bits.Add(_brand.Website!);
                    if (bits.Count > 0)
                        c.Item().PaddingTop(3).Text(string.Join("  ·  ", bits)).FontColor(OnDarkMuted).FontSize(7.5f);
                });
                row.ConstantItem(compact ? 150 : 190).AlignMiddle().Column(c =>
                {
                    c.Item().AlignRight().Text(title).FontColor("#ffffff").Bold().FontSize(compact ? 15 : 19);
                    c.Item().AlignRight().Text(number).FontColor(_brand.PrimaryHex).SemiBold().FontSize(10.5f).LetterSpacing(0.04f);
                    if (!string.IsNullOrEmpty(status))
                    {
                        var (bg, fg, label) = StatusPill(status!, ar);
                        c.Item().PaddingTop(5).AlignRight().Background(bg).PaddingVertical(2).PaddingHorizontal(9)
                            .Text(label).FontColor(fg).Bold().FontSize(7.5f).LetterSpacing(0.1f);
                    }
                });
            });
            col.Item().Height(3).Background(_brand.PrimaryHex);
        });
    }

    private static (string bg, string fg, string label) StatusPill(string status, bool ar)
    {
        var key = status.Trim().ToLowerInvariant();
        return key switch
        {
            "paid"                 => (Green, "#ffffff", ar ? "مدفوعة" : "PAID"),
            "overdue"              => (Red,   "#ffffff", ar ? "متأخرة" : "OVERDUE"),
            "pending"              => (Amber, "#ffffff", ar ? "مستحقة" : "DUE"),
            "partially paid"       => ("#f59e0b", "#ffffff", ar ? "مدفوعة جزئياً" : "PARTIALLY PAID"),
            "pending verification" => (Blue,  "#ffffff", ar ? "قيد التحقق" : "AWAITING CONFIRMATION"),
            "cancelled"            => ("#e2e8f0", "#475569", ar ? "ملغاة" : "CANCELLED"),
            _                      => ("#e2e8f0", "#475569", status.ToUpperInvariant()),
        };
    }

    private void MetaTile(IContainer container, string label, string value, string? valueColor = null)
    {
        container.Border(1).BorderColor(Line).Padding(9).Column(c =>
        {
            c.Item().Text(label).FontColor(Faint).FontSize(7.5f).Bold().LetterSpacing(0.08f);
            var v = c.Item().PaddingTop(1).Text(value).SemiBold().FontSize(10.5f);
            if (valueColor != null) v.FontColor(valueColor);
        });
    }

    private void Card(IContainer container, string title, Action<ColumnDescriptor> body)
    {
        container.Border(1).BorderColor(Line).Padding(10).Column(c =>
        {
            c.Spacing(1);
            c.Item().PaddingBottom(3).Text(title).FontColor(_brand.PrimaryHex).FontSize(7.5f).Bold().LetterSpacing(0.1f);
            body(c);
        });
    }

    private void SectionTitle(IContainer container, string title)
    {
        container.PaddingBottom(4).Row(r =>
        {
            r.AutoItem().Text(title).FontColor(_brand.PrimaryHex).FontSize(7.5f).Bold().LetterSpacing(0.1f);
            r.ConstantItem(8);
            r.RelativeItem().AlignMiddle().Height(1).Background(Line);
        });
    }

    private void PayCard(IContainer container, string title, string text, List<(string label, string value)>? lines = null)
    {
        container.Background(Soft).Border(1).BorderColor(Line).Padding(9).Column(c =>
        {
            c.Item().Text(title).SemiBold().FontSize(9.5f);
            if (lines != null)
                foreach (var (label, value) in lines)
                    c.Item().PaddingTop(1).Text(t =>
                    {
                        t.Span(label + ": ").FontColor(Faint).FontSize(8);
                        t.Span(value).FontSize(8.5f).SemiBold();
                    });
            // Small multi-line copy needs a little more leading than the
            // document default or the lines touch.
            c.Item().PaddingTop(2).Text(text).FontColor(Muted).FontSize(8).LineHeight(1.3f);
        });
    }

    private void DocFooter(IContainer container, bool ar, DateTime generatedAt, bool showTerms = true, bool compact = false)
    {
        string L(string en, string arText) => ar ? arText : en;
        container.PaddingTop(6).BorderTop(1).BorderColor(Line).Column(col =>
        {
            var terms = ar && !string.IsNullOrWhiteSpace(_brand.FooterTermsAr) ? _brand.FooterTermsAr : _brand.FooterTerms;
            if (showTerms && !string.IsNullOrWhiteSpace(terms))
                col.Item().PaddingTop(4).Text(terms!).FontSize(7).FontColor(Faint);
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7).FontColor(Faint));
                    t.Span(_brand.CompanyName);
                    t.Span("  ·  ");
                    if (!compact)
                    {
                        t.Span(L("Computer-generated document — no signature required", "مستند صادر إلكترونياً — لا يحتاج إلى توقيع"));
                        t.Span("  ·  ");
                    }
                    t.Span(L("Generated ", "صدر في ") + Dt(generatedAt, ar));
                });
                r.ConstantItem(70).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7).FontColor(Faint));
                    t.Span(L("Page ", "صفحة "));
                    t.CurrentPageNumber();
                    t.Span(L(" of ", " من "));
                    t.TotalPages();
                });
            });
        });
    }

    private static bool Is(string? status, string expected) =>
        string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string? JoinPresent(string sep, params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join(sep, present);
    }

    /// <summary>
    /// Legacy header used by the executive dashboard and tenant statement
    /// renderers below. Routed through the same brand band so all four
    /// documents share the logo, colours and company block.
    /// </summary>
    private void Header(IContainer container, string title, string number, string? _legacyCompanyName)
        => BrandBand(container, title, number, status: null, ar: false);

    // ─── Sprint 3 — executive dashboard PDF ─────────────────────────────
    public byte[] RenderExecutiveDashboard(ExecutiveDashboardPdfModel m) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(s => s.FontSize(10).FontColor("#1e293b"));

            page.Header().Element(c => Header(c, "EXECUTIVE DASHBOARD",
                $"{m.FromDate:dd MMM yyyy} – {m.ToDate:dd MMM yyyy}", m.CompanyName));

            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(14);

                // 4 KPI tiles in a row
                col.Item().Row(r =>
                {
                    r.Spacing(8);
                    r.RelativeItem().Element(c => Kpi(c, "Collected", $"{m.Collected:N3} {m.Currency}",
                        m.CollectedDelta == 0 ? null : $"{(m.CollectedDelta > 0 ? "▲" : "▼")} {Math.Abs(m.CollectedDelta):0.0}%"));
                    r.RelativeItem().Element(c => Kpi(c, "Outstanding", $"{m.Outstanding:N3} {m.Currency}", null));
                    r.RelativeItem().Element(c => Kpi(c, "Overdue", m.OverdueCount.ToString(), null));
                    r.RelativeItem().Element(c => Kpi(c, "Occupancy",
                        $"{m.OccupancyPercent:0.0}%", $"{m.OccupiedUnits} / {m.TotalUnits} units"));
                });

                // 12-month revenue trend (table — PDF doesn't draw charts)
                col.Item().Text("Revenue — last 12 months").SemiBold().FontSize(12).FontColor("#475569");
                col.Item().Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        foreach (var _ in m.Trend) cols.RelativeColumn();
                    });
                    foreach (var (label, _) in m.Trend)
                        t.Cell().AlignCenter().Text(label).FontSize(8).FontColor("#94a3b8");
                    foreach (var (_, value) in m.Trend)
                        t.Cell().AlignCenter().Text($"{value:N0}").SemiBold();
                });

                // Status breakdown + top properties side by side
                col.Item().Row(r =>
                {
                    r.Spacing(12);
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Invoices by status").SemiBold().FontSize(12).FontColor("#475569");
                        c.Item().Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(); });
                            t.Header(h =>
                            {
                                h.Cell().Text("Status").SemiBold().FontColor("#64748b").FontSize(9);
                                h.Cell().AlignRight().Text("Count").SemiBold().FontColor("#64748b").FontSize(9);
                            });
                            foreach (var (status, count) in m.StatusBreakdown)
                            {
                                t.Cell().Text(status);
                                t.Cell().AlignRight().Text(count.ToString());
                            }
                        });
                    });
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Top 5 properties").SemiBold().FontSize(12).FontColor("#475569");
                        c.Item().Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(2); });
                            t.Header(h =>
                            {
                                h.Cell().Text("Property").SemiBold().FontColor("#64748b").FontSize(9);
                                h.Cell().AlignRight().Text("Collected").SemiBold().FontColor("#64748b").FontSize(9);
                            });
                            foreach (var (name, value) in m.TopProperties)
                            {
                                t.Cell().Text(name);
                                t.Cell().AlignRight().Text($"{value:N3} {m.Currency}");
                            }
                        });
                    });
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Generated ").FontColor("#94a3b8").FontSize(8);
                t.Span(DateTime.UtcNow.ToString("dd MMM yyyy HH:mm")).FontColor("#94a3b8").FontSize(8);
                t.Span("  ·  page ").FontColor("#94a3b8").FontSize(8);
                t.CurrentPageNumber().FontColor("#94a3b8").FontSize(8);
                t.Span(" / ").FontColor("#94a3b8").FontSize(8);
                t.TotalPages().FontColor("#94a3b8").FontSize(8);
            });
        });
    }).GeneratePdf();

    private static void Kpi(IContainer container, string label, string value, string? subtitle)
    {
        container.Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(10).Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(8).FontColor("#94a3b8").SemiBold();
            col.Item().Text(value).FontSize(14).SemiBold().FontColor("#0f172a");
            if (!string.IsNullOrEmpty(subtitle))
                col.Item().Text(subtitle).FontSize(8).FontColor("#64748b");
        });
    }

    // ─── Sprint 3 (Tenants) — payment statement PDF ─────────────────────
    public byte[] RenderTenantStatement(TenantStatementPdfModel m) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(s => s.FontSize(10).FontColor("#1e293b"));

            page.Header().Element(c => Header(c, "TENANT STATEMENT",
                $"#{m.TenantId:D6}", m.CompanyName));

            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(14);

                // Tenant + outstanding side-by-side
                col.Item().Row(r =>
                {
                    r.Spacing(12);
                    r.RelativeItem(2).Border(1).BorderColor("#e2e8f0").Padding(10).Column(c =>
                    {
                        c.Item().Text(m.TenantName).FontSize(14).SemiBold();
                        if (!string.IsNullOrEmpty(m.Email))
                            c.Item().Text($"Email: {m.Email}").FontSize(9).FontColor("#475569");
                        if (!string.IsNullOrEmpty(m.Phone))
                            c.Item().Text($"Phone: {m.Phone}").FontSize(9).FontColor("#475569");
                        if (!string.IsNullOrEmpty(m.PACINumber))
                            c.Item().Text($"Client PACI: {m.PACINumber}").FontSize(9).FontColor("#475569");
                        c.Item().Text($"Statement as of {m.GeneratedAt:dd MMM yyyy HH:mm} UTC")
                            .FontSize(8).FontColor("#94a3b8").Italic();
                    });
                    r.RelativeItem().Element(c => Kpi(c, "Outstanding",
                        $"{m.Outstanding:N3} {m.Currency}", m.Outstanding > 0 ? "balance due" : "all settled"));
                });

                // Active leases
                col.Item().Text("Active leases").SemiBold().FontSize(12).FontColor("#475569");
                if (m.Leases.Count == 0)
                {
                    col.Item().PaddingVertical(8).Text("No active leases on file.")
                        .FontColor("#94a3b8").Italic();
                }
                else
                {
                    col.Item().Border(1).BorderColor("#e2e8f0").Padding(0).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(3); cd.RelativeColumn(); cd.RelativeColumn(2);
                            cd.RelativeColumn(2); cd.RelativeColumn(2);
                        });
                        t.Header(h =>
                        {
                            void H(string label) => h.Cell().Background("#f8fafc")
                                .Padding(6).Text(label).SemiBold().FontSize(9).FontColor("#64748b");
                            H("Property"); H("Unit"); H("Start"); H("End"); H("Monthly Rent");
                        });
                        foreach (var l in m.Leases)
                        {
                            t.Cell().Padding(6).Text(l.Property);
                            t.Cell().Padding(6).Text(l.Unit);
                            t.Cell().Padding(6).Text($"{l.LeaseStartDate:dd MMM yyyy}");
                            t.Cell().Padding(6).Text($"{l.LeaseEndDate:dd MMM yyyy}");
                            t.Cell().Padding(6).AlignRight().Text($"{l.MonthlyRent:N3} {m.Currency}");
                        }
                    });
                }

                // Recent payments
                col.Item().Text($"Recent payments (last {m.RecentPayments.Count})")
                    .SemiBold().FontSize(12).FontColor("#475569");
                if (m.RecentPayments.Count == 0)
                {
                    col.Item().PaddingVertical(8).Text("No completed payments recorded.")
                        .FontColor("#94a3b8").Italic();
                }
                else
                {
                    col.Item().Border(1).BorderColor("#e2e8f0").Padding(0).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(3);
                        });
                        t.Header(h =>
                        {
                            void H(string label) => h.Cell().Background("#f8fafc")
                                .Padding(6).Text(label).SemiBold().FontSize(9).FontColor("#64748b");
                            H("Date"); H("Amount"); H("Method"); H("Reference");
                        });
                        foreach (var p in m.RecentPayments)
                        {
                            t.Cell().Padding(6).Text($"{p.PaymentDate:dd MMM yyyy}");
                            t.Cell().Padding(6).AlignRight().Text($"{p.AmountPaid:N3} {m.Currency}");
                            t.Cell().Padding(6).Text(p.Method);
                            t.Cell().Padding(6).Text(p.Reference ?? "—").FontColor("#64748b").FontSize(9);
                        }
                    });
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Generated ").FontColor("#94a3b8").FontSize(8);
                t.Span(m.GeneratedAt.ToString("dd MMM yyyy HH:mm")).FontColor("#94a3b8").FontSize(8);
                t.Span("  ·  page ").FontColor("#94a3b8").FontSize(8);
                t.CurrentPageNumber().FontColor("#94a3b8").FontSize(8);
                t.Span(" / ").FontColor("#94a3b8").FontSize(8);
                t.TotalPages().FontColor("#94a3b8").FontSize(8);
            });
        });
    }).GeneratePdf();
}
