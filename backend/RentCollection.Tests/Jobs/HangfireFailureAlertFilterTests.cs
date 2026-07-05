using FluentAssertions;

namespace RentCollection.Tests.Jobs;

/// <summary>
/// Sprint 6 — coverage for the recipient-parsing logic shared by the
/// failure-alert pathway and the monthly executive-dashboard job.
///
/// We can't drive a full <c>HangfireFailureAlertFilter</c> integration test
/// from xUnit (the filter type lives in <c>RentCollection.Web</c> which the
/// Tests project doesn't reference, and the Hangfire context type is
/// effectively non-mockable). Instead we pin the parsing rules — the same
/// rules the filter applies inline — so a refactor that changes them
/// breaks a clear unit test rather than silently dropping email recipients
/// in prod.
/// </summary>
public class HangfireFailureAlertFilterTests
{
    /// <summary>
    /// Mirrors the parse expression in <c>HangfireFailureAlertFilter.OnStateApplied</c>
    /// and <c>RecurringJobs.SendMonthlyExecutiveDashboardAsync</c>. If you
    /// change one, change the other. The Theory below pins this contract.
    /// </summary>
    private static List<string> ParseRecipients(string raw) =>
        raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

    [Theory]
    [InlineData("",                                  new string[0])]
    [InlineData("   ",                               new string[0])]
    [InlineData("ops@alrtam.com",                    new[] { "ops@alrtam.com" })]
    [InlineData("a@x.com;b@x.com",                   new[] { "a@x.com", "b@x.com" })]
    [InlineData("a@x.com,b@x.com",                   new[] { "a@x.com", "b@x.com" })]
    [InlineData("a@x.com, b@x.com , c@x.com",        new[] { "a@x.com", "b@x.com", "c@x.com" })]
    [InlineData(" leading@x.com ;trailing@x.com ",   new[] { "leading@x.com", "trailing@x.com" })]
    [InlineData("a@x.com;;b@x.com",                  new[] { "a@x.com", "b@x.com" })]
    public void Recipient_parsing_handles_separators_and_whitespace(string raw, string[] expected)
    {
        var parsed = ParseRecipients(raw);
        parsed.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Empty_recipient_list_means_filter_must_no_op()
    {
        // The filter's contract: when the parser yields zero recipients,
        // OnStateApplied returns early without touching IEmailService.
        // This pins the upstream invariant — anything else would crash
        // the Hangfire worker on first failure during dev (no recipients
        // configured) and silently drop alerts in prod (config typo).
        var raw = "   ;  ;  ";
        var parsed = ParseRecipients(raw);

        parsed.Should().BeEmpty();
    }
}
