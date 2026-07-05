namespace RentCollection.Application.Services;

/// <summary>
/// Pure-function matcher that pairs our internal <c>Payment</c> records with
/// K-NET's <c>KNETReconciliation</c> bank-settlement rows. Used by the daily
/// reconciliation report and the operator-facing diff page.
///
/// The matching key is <c>CheckoutPaymentId</c> — the gateway-issued ID K-NET
/// puts in both the original callback and the settlement file. <c>TrackId</c>
/// would also work but is our identifier, not K-NET's, so it's vulnerable to
/// data-entry mistakes in the bank file.
///
/// Returns three buckets:
///   • Matched      — payment + bank line pair, status OK or mismatch noted
///   • Unmatched    — completed payment with no matching bank line
///   • Orphaned     — bank line with no corresponding payment record
///
/// Operator response to each bucket:
///   Matched(mismatch)  → reconcile amount discrepancy
///   Unmatched          → the bank hasn't settled yet, OR our row is fraudulent
///   Orphaned           → real money landed in our account, we have no record
///                        — investigate before the auditor finds it
/// </summary>
public static class ReconciliationMatcher
{
    public record PaymentLine(int PaymentId, string? CheckoutPaymentId, decimal Amount, DateTime PaymentDate);
    public record BankLine(int ReconciliationId, string? CheckoutPaymentId, decimal Amount, DateTime TransactionDate);
    public record Pair(PaymentLine Payment, BankLine Bank, decimal AmountDelta);
    public record Result(
        IReadOnlyList<Pair> Matched,
        IReadOnlyList<PaymentLine> UnmatchedPayments,
        IReadOnlyList<BankLine> OrphanedBankLines);

    /// <summary>
    /// Match payments to bank lines by <see cref="PaymentLine.CheckoutPaymentId"/>.
    /// Both sides are joined on that key; entries with a null/empty key go
    /// into the unmatched/orphaned buckets unconditionally (a payment with no
    /// gateway ID can't be reconciled either way).
    /// </summary>
    public static Result Match(
        IEnumerable<PaymentLine> payments,
        IEnumerable<BankLine> bankLines)
    {
        var paymentList = payments.ToList();
        var bankList = bankLines.ToList();

        // Index bank lines by checkout payment id. Multiple bank lines for
        // the same checkoutPaymentId means K-NET resubmitted — we pair
        // against the first one and treat the rest as orphans so the
        // operator notices the duplicate.
        var bankByKey = new Dictionary<string, BankLine>(StringComparer.OrdinalIgnoreCase);
        var orphans = new List<BankLine>();
        foreach (var bank in bankList)
        {
            if (string.IsNullOrEmpty(bank.CheckoutPaymentId))
            {
                orphans.Add(bank);
                continue;
            }
            if (bankByKey.ContainsKey(bank.CheckoutPaymentId))
            {
                orphans.Add(bank); // duplicate settlement — surface for review
            }
            else
            {
                bankByKey[bank.CheckoutPaymentId] = bank;
            }
        }

        var matched = new List<Pair>();
        var unmatched = new List<PaymentLine>();
        var consumedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var payment in paymentList)
        {
            if (string.IsNullOrEmpty(payment.CheckoutPaymentId))
            {
                unmatched.Add(payment);
                continue;
            }
            if (bankByKey.TryGetValue(payment.CheckoutPaymentId, out var bank))
            {
                matched.Add(new Pair(payment, bank, payment.Amount - bank.Amount));
                consumedKeys.Add(payment.CheckoutPaymentId);
            }
            else
            {
                unmatched.Add(payment);
            }
        }

        // Bank lines that survived the match step are orphans.
        foreach (var (key, bank) in bankByKey)
        {
            if (!consumedKeys.Contains(key)) orphans.Add(bank);
        }

        return new Result(matched, unmatched, orphans);
    }
}
