using FluentAssertions;
using RentCollection.Infrastructure.Services;

namespace RentCollection.Tests.Knet;

/// <summary>
/// Sprint 6 — contract coverage for the K-NET payment gateway integration.
///
/// K-NET is the only payment gateway in production for tenants in Kuwait.
/// A regression in our request encryption silently makes every payment
/// fail at the gateway — the user sees a generic error and we don't know why.
/// These tests guard against that.
///
/// Split into two tiers:
///   • Self-check (always runs) — round-trips a payload through our own
///     <see cref="KnetUtil"/> Encrypt/Decrypt with a known fixture key so a
///     bug in either side would fail a CI gate.
///   • Sandbox probe (env-gated) — when <c>KNET_SANDBOX_*</c> env vars are
///     set, makes a real HTTP request to the K-NET test gateway URL and
///     asserts a non-error response. Wired into a nightly job — failures
///     here mean K-NET changed their endpoint or our creds were rotated.
/// </summary>
[Trait("Category", "Contract")]
public class KnetContractTests
{
    private const string FixtureKey = "0123456789ABCDEF";  // 16-char AES-128 key
    private readonly KnetUtil _knet = new();

    [Fact]
    public void Encrypted_payload_round_trips_through_decrypt_unchanged()
    {
        // The end-to-end correctness property: anything we send out, we
        // could decrypt — proves the AES-CBC config + hex encoding are
        // self-consistent. If KNET ever sends us a callback we need to
        // decrypt, this same path runs in reverse.
        var clear = "id=test&amt=10.500&trackid=ABC123&udf1=lease42";

        var encrypted = _knet.EncryptUrlParams(clear, FixtureKey);
        var roundTripped = _knet.DecryptUrlParams(encrypted, FixtureKey);

        roundTripped.Should().Be(clear);
    }

    [Fact]
    public void Encrypted_payload_is_uppercase_hex_with_no_separators()
    {
        // KNET's reference implementation expects uppercase hex without
        // separators. A wrong format here means the gateway rejects the
        // request before payment even starts. This test pins the format
        // so a well-meaning refactor to Convert.ToHexString (lowercase)
        // doesn't ship.
        var clear = "id=test&amt=10.500";

        var encrypted = _knet.EncryptUrlParams(clear, FixtureKey);

        encrypted.Should().MatchRegex("^[0-9A-F]+$",
            "K-NET expects uppercase hex; the gateway rejects lowercase or " +
            "hex with dashes/colons");
        (encrypted.Length % 2).Should().Be(0,
            "hex output must be byte-aligned (even length)");
    }

    [Fact]
    public void GenerateKnetUrl_emits_all_required_fields_in_clear_routing_segment()
    {
        // The redirect URL has an encrypted blob plus a clear-text routing
        // tail (`tranportalId`, `responseURL`, `errorURL`) that K-NET reads
        // BEFORE decrypting. If we lose any of those the gateway can't
        // route the response back to us — payment "succeeds" but the
        // tenant lands on an error page forever.
        var url = _knet.GenerateKnetUrl(
            tranportalId: "TID_TEST",
            tranportalPwd: "PWD_TEST",
            terminalResourceKey: FixtureKey,
            paymentApiUrl: "https://kpaytest.com.kw/kpg/PaymentHTTP.htm?param=paymentInit",
            amount: 12.345m,
            trackId: "TRK_42",
            responseUrl: "https://example.com/ok",
            errorUrl: "https://example.com/err");

        url.Should().StartWith("https://kpaytest.com.kw/kpg/PaymentHTTP.htm");
        url.Should().Contain("trandata=");
        url.Should().Contain("tranportalId=TID_TEST");
        url.Should().Contain("responseURL=https://example.com/ok");
        url.Should().Contain("errorURL=https://example.com/err");
    }

    [Fact]
    public void Cleartext_uses_KWD_3_decimal_precision()
    {
        // KWD has 3 decimal places (5.250 KD, not 5.25). K-NET rejects
        // 2-decimal amounts because Kuwait fil-level precision is essential
        // for fractional payments. The bug manifests as "amount mismatch"
        // errors at the gateway only — not at compile time. Pin the format.
        var url = _knet.GenerateKnetUrl(
            tranportalId: "T", tranportalPwd: "P",
            terminalResourceKey: FixtureKey,
            paymentApiUrl: "https://x",
            amount: 5m,
            trackId: "T",
            responseUrl: "https://x", errorUrl: "https://x");

        // Round-trip the encrypted payload to inspect the cleartext we sent.
        var trandata = ExtractTrandata(url);
        var clear = _knet.DecryptUrlParams(trandata, FixtureKey);

        clear.Should().Contain("amt=5.000",
            "KWD requires 3-decimal precision (fils); 2-decimal amounts " +
            "are rejected by the gateway");
        clear.Should().Contain("currencycode=414",
            "ISO 4217 numeric code for KWD; hard-coded by K-NET spec");
    }

    [Fact]
    public void Parse_response_params_round_trip_through_KNET_callback_shape()
    {
        // Mimics what comes back from K-NET in a redirect: ampersand-delimited
        // key=value pairs. Case-insensitive so callback variations like
        // `Result` vs `result` are tolerated.
        var sample = "paymentid=12345&result=CAPTURED&amt=10.500&trackid=TRK_42";

        var parsed = _knet.ParseResponseParams(sample);

        parsed["paymentid"].Should().Be("12345");
        parsed["RESULT"].Should().Be("CAPTURED", "parser must be case-insensitive");
        parsed["amt"].Should().Be("10.500");
        parsed["trackid"].Should().Be("TRK_42");
    }

    // ───────────── Sprint 19 — additional contract pins ─────────────

    [Theory]
    [InlineData("en",  "USA")]
    [InlineData("EN",  "USA")]
    [InlineData(null,  "USA")]
    [InlineData("",    "USA")]
    [InlineData("ar",  "ARA")]
    [InlineData("AR",  "ARA")]
    [InlineData("ara", "ARA")]
    public void Language_code_maps_to_KNET_expected_value(string? langInput, string expectedInUrl)
    {
        // KNET uses USA / ARA — NOT en / ar / en-US. Drift here means the
        // gateway renders the wrong language and tenants type their card
        // details into a form they can't read.
        var url = _knet.GenerateKnetUrl(
            tranportalId: "TID", tranportalPwd: "PWD", terminalResourceKey: FixtureKey,
            paymentApiUrl: "https://x", amount: 5.000m, trackId: "T",
            responseUrl: "https://x", errorUrl: "https://x",
            lang: langInput!);

        var trandata = ExtractTrandata(url);
        var clear = _knet.DecryptUrlParams(trandata, FixtureKey);
        clear.Should().Contain($"langid={expectedInUrl}");
    }

    [Theory]
    [InlineData("paymentid=12345")]                              // missing trailing &
    [InlineData("paymentid=12345&")]                             // trailing &
    [InlineData("&paymentid=12345")]                             // leading &
    [InlineData("paymentid=12345&&result=CAPTURED")]             // empty middle segment
    [InlineData("paymentid=12345&malformed&result=CAPTURED")]    // segment with no =
    public void Parser_handles_malformed_segments_without_throwing(string raw)
    {
        // K-NET callbacks have varied across releases — defensive parsing
        // is the difference between a clean rejection and a 500 → silent
        // payment loss. The parser must NOT throw on any of these shapes.
        var act = () => _knet.ParseResponseParams(raw);
        act.Should().NotThrow();

        var parsed = _knet.ParseResponseParams(raw);
        parsed.Should().ContainKey("paymentid")
            .WhoseValue.Should().Be("12345",
                "the well-formed segments must still parse even when others are malformed");
    }

    [Fact]
    public void Empty_callback_returns_empty_dict_not_null()
    {
        // Defensive: the dispatcher in HandleKnetResponseAsync does
        // `parsed.GetValueOrDefault(...)` everywhere — relies on a non-null
        // dictionary. A null return here would NRE the handler.
        var parsed = _knet.ParseResponseParams("");

        parsed.Should().NotBeNull();
        parsed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://kpaytest.com.kw/kpg/PaymentHTTP.htm?param=paymentInit")]
    [InlineData("https://www.kpay.com.kw/kpg/PaymentHTTP.htm?param=paymentInit")]
    public void Generated_url_preserves_gateway_query_string(string gatewayUrl)
    {
        // The gateway URL ships with `?param=paymentInit`. We append our
        // `&trandata=...` segment. Test that the gateway's own param is
        // preserved exactly — losing it would cause the gateway to reject
        // the request with "missing param" before even decrypting.
        var url = _knet.GenerateKnetUrl(
            tranportalId: "TID", tranportalPwd: "PWD", terminalResourceKey: FixtureKey,
            paymentApiUrl: gatewayUrl, amount: 1.000m, trackId: "T",
            responseUrl: "https://x", errorUrl: "https://x");

        url.Should().Contain("param=paymentInit",
            "the gateway's own query param must be preserved verbatim in our URL");
        url.Should().Contain("&trandata=",
            "our trandata segment must be appended with the ampersand separator");
    }

    // ───────────── Sandbox connectivity probe (env-gated) ─────────────

    [Fact]
    public async Task Sandbox_gateway_url_responds_when_credentials_configured()
    {
        // Sandbox creds are NEVER baked into the repo. Set them in CI env
        // for the dedicated K-NET contract job; locally this test no-ops.
        // We can't use [SkippableFact] without an extra package, so we
        // return early — xUnit reports this as Passed, which is the
        // right semantic here: "no creds = nothing to verify, not broken".
        if (!HasSandboxCreds())
        {
            // Surface the no-op via an attached output so CI logs explain it.
            Console.WriteLine("[skip] KNET_SANDBOX_* env vars not set — sandbox probe not run.");
            return;
        }

        var tid = Environment.GetEnvironmentVariable("KNET_SANDBOX_TRANPORTAL_ID")!;
        var pwd = Environment.GetEnvironmentVariable("KNET_SANDBOX_TRANPORTAL_PWD")!;
        var key = Environment.GetEnvironmentVariable("KNET_SANDBOX_KEY")!;

        var url = _knet.GenerateKnetUrl(
            tranportalId: tid, tranportalPwd: pwd, terminalResourceKey: key,
            paymentApiUrl: "https://kpaytest.com.kw/kpg/PaymentHTTP.htm?param=paymentInit",
            amount: 1.000m, trackId: $"smoke-{DateTime.UtcNow.Ticks}",
            responseUrl: "https://example.com/ok",
            errorUrl: "https://example.com/err");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.GetAsync(url);

        // The gateway should respond at all — a 200 (with an HTML form), a
        // 302 (redirect to confirmation), or even a 4xx (with an error
        // page) all prove TCP + TLS + DNS + URL shape are correct. A
        // ConnectFailure / timeout would mean the gateway moved.
        ((int)response.StatusCode).Should().BeInRange(200, 499,
            "any well-formed HTTP response proves the gateway endpoint is reachable; " +
            "5xx or transport failures indicate K-NET-side or network issues");
    }

    /// <summary>Extract the trandata=&lt;hex&gt; segment for round-trip inspection.</summary>
    private static string ExtractTrandata(string url)
    {
        var marker = "trandata=";
        var idx = url.IndexOf(marker);
        if (idx < 0) throw new InvalidOperationException("no trandata in URL");
        var rest = url[(idx + marker.Length)..];
        // The clear-text routing segment starts at the first '&' AFTER the hex blob.
        var amp = rest.IndexOf('&');
        return amp < 0 ? rest : rest[..amp];
    }

    private static bool HasSandboxCreds() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KNET_SANDBOX_TRANPORTAL_ID"))
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KNET_SANDBOX_TRANPORTAL_PWD"))
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KNET_SANDBOX_KEY"));
}
