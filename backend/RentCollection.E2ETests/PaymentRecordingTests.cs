using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 16 — browser coverage of the Cash payment recording workflow.
/// Bank Transfer + KNET initiate flows have their own dedicated suites
/// (KNETPayment via integration tests; bank transfer covered in
/// <see cref="BankTransferApprovalTests"/>).
/// </summary>
public sealed class PaymentRecordingTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public PaymentRecordingTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Record_payment_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/CashPayment/Record");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Record_form_renders_invoice_amount_and_date_fields()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/CashPayment/Record");

        (await page.Locator("[name='InvoiceId']").CountAsync()).Should().Be(1);
        (await page.Locator("[name='Amount']").CountAsync()).Should().Be(1);
        (await page.Locator("[name='PaymentDate']").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Amount_input_uses_KWD_3_decimal_precision()
    {
        // K-NET amount-mismatch guard (Sprint 8 Fix #5) requires bit-perfect
        // agreement. The Cash form must use the same precision.
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/CashPayment/Record");

        var step = await page.Locator("[name='Amount']").GetAttributeAsync("step");
        step.Should().Be("0.001", "KWD is 3-decimal precision — input must allow fil-level entry");
    }

    [Fact]
    public async Task Submit_with_no_invoice_selected_shows_validation_error()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        await page.GotoAsync("/CashPayment/Record");

        await page.Locator("[name='Amount']").FillAsync("100");
        await page.GetByRole(AriaRole.Button, new() { Name = "Record" }).First.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        page.Url.Should().Contain("/CashPayment/Record",
            "submit without an invoice must NOT silently succeed");
    }
}
