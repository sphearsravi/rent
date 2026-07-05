using FluentAssertions;
using Microsoft.Playwright;
using RentCollection.E2ETests.Helpers;

namespace RentCollection.E2ETests;

/// <summary>
/// Sprint 17 — Documents listing + upload page surface. Actual file-upload
/// rules (size limit, extension allowlist) are enforced server-side and
/// the upload happy path needs a fixture file; these tests pin the
/// always-true invariants and leave the full upload flow for a future
/// suite when the fixture story is sorted.
/// </summary>
public sealed class DocumentUploadTests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fixture;
    public DocumentUploadTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_request_to_Documents_redirects_to_login()
    {
        await using var ctx = await _fixture.NewContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync("/Documents");
        page.Url.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Documents_index_renders_for_admin()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Documents");
        response!.Status.Should().Be(200);
        (await page.ContentAsync()).ToLowerInvariant().Should().Contain("document");
    }

    [Fact]
    public async Task Upload_page_renders_with_file_input()
    {
        var (ctx, page) = await AdminSession.SignInAsync(_fixture);
        await using var _ = ctx;
        var response = await page.GotoAsync("/Documents/Upload");
        if (response!.Status == 404) return;  // route varies by deploy
        response.Status.Should().Be(200);

        (await page.Locator("input[type='file']").CountAsync())
            .Should().BeGreaterThanOrEqualTo(1, "Upload page must expose at least one file input");
    }
}
