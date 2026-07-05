namespace RentCollection.Application.Interfaces;

/// <summary>
/// Sprint 24 — enforces the "oldest unpaid first" rule. Every payment
/// entry point (K-NET init, cash record, bank-transfer submit + approval)
/// MUST call <see cref="CheckAsync"/> before persisting a payment.
///
/// Rule: a tenant cannot pay any invoice that isn't their oldest
/// unpaid one. "Unpaid" = Status not Paid and not Cancelled (i.e.
/// Pending, PartiallyPaid, Overdue all queue). Partial payment of
/// the oldest still blocks newer ones.
/// </summary>
public interface IPaymentEligibilityService
{
    Task<EligibilityResult> CheckAsync(int invoiceId, CancellationToken ct = default);
}

/// <summary>
/// Result of an eligibility check. When <see cref="IsEligible"/> is
/// false, the other fields describe the invoice the tenant must pay
/// first so callers can render a useful error message.
/// </summary>
public sealed record EligibilityResult(
    bool IsEligible,
    int? OldestUnpaidInvoiceId = null,
    decimal? OldestBalance = null,
    string? OldestPeriodLabel = null,
    string? Reason = null
)
{
    public static EligibilityResult Eligible() => new(true);

    public static EligibilityResult Blocked(int oldestId, decimal balance, string period) =>
        new(false, oldestId, balance, period,
            $"Pay invoice #{oldestId} ({period}, KWD {balance:0.000} outstanding) first.");

    public static EligibilityResult InvoiceNotFound() =>
        new(false, Reason: "Invoice not found.");
}
