using FluentAssertions;
using RentCollection.Application.Services;

namespace RentCollection.Tests.Knet;

/// <summary>
/// Sprint 7 Tier 4 — coverage for <see cref="ReconciliationMatcher.Match"/>.
/// Pure functions, no DB — pin the matching algebra so the operator-facing
/// reconciliation diff page has provable correctness.
/// </summary>
public class ReconciliationMatcherTests
{
    [Fact]
    public void Matched_pair_appears_in_Matched_bucket_with_zero_delta()
    {
        var payment = new ReconciliationMatcher.PaymentLine(
            PaymentId: 1, CheckoutPaymentId: "KP_OK", Amount: 500m, PaymentDate: new DateTime(2026, 5, 1));
        var bank = new ReconciliationMatcher.BankLine(
            ReconciliationId: 10, CheckoutPaymentId: "KP_OK", Amount: 500m, TransactionDate: new DateTime(2026, 5, 2));

        var result = ReconciliationMatcher.Match(new[] { payment }, new[] { bank });

        result.Matched.Should().HaveCount(1);
        result.Matched[0].AmountDelta.Should().Be(0m);
        result.UnmatchedPayments.Should().BeEmpty();
        result.OrphanedBankLines.Should().BeEmpty();
    }

    [Fact]
    public void Matched_pair_with_amount_drift_surfaces_delta_does_not_drop_to_orphan()
    {
        // Our payment record says 500.000, bank settled 499.500 — a real
        // reconciliation issue (perhaps a 0.5 KWD network surcharge). Must
        // pair them up so the operator can see the delta, NOT throw them
        // into unmatched/orphan piles.
        var payment = new ReconciliationMatcher.PaymentLine(1, "KP_DRIFT", 500m, new DateTime(2026, 5, 1));
        var bank = new ReconciliationMatcher.BankLine(10, "KP_DRIFT", 499.5m, new DateTime(2026, 5, 2));

        var result = ReconciliationMatcher.Match(new[] { payment }, new[] { bank });

        result.Matched.Should().HaveCount(1);
        result.Matched[0].AmountDelta.Should().Be(0.5m,
            "delta = payment - bank; positive means we expected more than the bank credited");
    }

    [Fact]
    public void Payment_with_no_matching_bank_line_goes_to_UnmatchedPayments()
    {
        // Operator interpretation: bank hasn't settled yet OR our row is
        // suspect. Either way the operator must see it.
        var payment = new ReconciliationMatcher.PaymentLine(1, "KP_UNMATCHED", 500m, new DateTime(2026, 5, 1));

        var result = ReconciliationMatcher.Match(new[] { payment }, Array.Empty<ReconciliationMatcher.BankLine>());

        result.Matched.Should().BeEmpty();
        result.UnmatchedPayments.Should().ContainSingle(p => p.CheckoutPaymentId == "KP_UNMATCHED");
        result.OrphanedBankLines.Should().BeEmpty();
    }

    [Fact]
    public void Bank_line_with_no_matching_payment_goes_to_OrphanedBankLines()
    {
        // This is the worst-case bucket — real money settled in our bank
        // account but we have no record of taking it. Could be a missed
        // callback, could be a misrouted payment from another merchant.
        // The auditor will find this; better us first.
        var bank = new ReconciliationMatcher.BankLine(10, "KP_ORPHAN", 500m, new DateTime(2026, 5, 2));

        var result = ReconciliationMatcher.Match(Array.Empty<ReconciliationMatcher.PaymentLine>(), new[] { bank });

        result.OrphanedBankLines.Should().ContainSingle(b => b.CheckoutPaymentId == "KP_ORPHAN");
        result.UnmatchedPayments.Should().BeEmpty();
        result.Matched.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_bank_lines_for_same_payment_id_pair_first_and_orphan_rest()
    {
        // K-NET re-submitted the same settlement line — pair the first one
        // with our payment, surface the second as an orphan so the
        // operator can chase the double settlement before it shows up in
        // the bank statement.
        var payment = new ReconciliationMatcher.PaymentLine(1, "KP_DUPE", 500m, new DateTime(2026, 5, 1));
        var bank1 = new ReconciliationMatcher.BankLine(10, "KP_DUPE", 500m, new DateTime(2026, 5, 2));
        var bank2 = new ReconciliationMatcher.BankLine(11, "KP_DUPE", 500m, new DateTime(2026, 5, 3));

        var result = ReconciliationMatcher.Match(new[] { payment }, new[] { bank1, bank2 });

        result.Matched.Should().HaveCount(1);
        result.OrphanedBankLines.Should().ContainSingle(b => b.ReconciliationId == 11,
            "the second copy is a duplicate settlement — operator must see it");
    }

    [Fact]
    public void Mixed_realistic_batch_splits_into_all_three_buckets()
    {
        // 5 payments, 5 bank lines, partial overlap — the real-world shape
        // of a daily batch. Pin the exact splits so a regression that
        // re-arranges the buckets fails loud.
        var payments = new[]
        {
            new ReconciliationMatcher.PaymentLine(1, "KP_A", 100m, new DateTime(2026, 5, 1)),
            new ReconciliationMatcher.PaymentLine(2, "KP_B", 200m, new DateTime(2026, 5, 1)),
            new ReconciliationMatcher.PaymentLine(3, "KP_C", 300m, new DateTime(2026, 5, 1)),
            new ReconciliationMatcher.PaymentLine(4, "KP_NOBANK", 400m, new DateTime(2026, 5, 1)),
            new ReconciliationMatcher.PaymentLine(5, null,        500m, new DateTime(2026, 5, 1)),
        };
        var bankLines = new[]
        {
            new ReconciliationMatcher.BankLine(10, "KP_A", 100m, new DateTime(2026, 5, 2)),
            new ReconciliationMatcher.BankLine(11, "KP_B", 200m, new DateTime(2026, 5, 2)),
            new ReconciliationMatcher.BankLine(12, "KP_C", 299.5m, new DateTime(2026, 5, 2)), // drift
            new ReconciliationMatcher.BankLine(13, "KP_ORPHAN", 700m, new DateTime(2026, 5, 2)),
            new ReconciliationMatcher.BankLine(14, null, 800m, new DateTime(2026, 5, 2)),
        };

        var result = ReconciliationMatcher.Match(payments, bankLines);

        result.Matched.Should().HaveCount(3, "KP_A, KP_B, KP_C all paired");
        result.UnmatchedPayments.Should().HaveCount(2, "KP_NOBANK + the null-checkout payment");
        result.OrphanedBankLines.Should().HaveCount(2, "KP_ORPHAN + the null-checkout bank line");
        result.Matched.Single(p => p.Payment.CheckoutPaymentId == "KP_C")
            .AmountDelta.Should().Be(0.5m, "C had a 0.5 KWD drift");
    }
}
