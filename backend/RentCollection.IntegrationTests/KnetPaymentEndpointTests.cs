using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RentCollection.IntegrationTests;

/// <summary>
/// Sprint 7 Tier 3 — integration coverage for the K-NET payment endpoints.
/// These exercise the public surface that K-NET (or, worryingly, anyone on
/// the internet) can POST to. Each test enforces the security boundary at
/// the HTTP layer — the callback handler unit tests prove the per-payment
/// behaviour; these prove "the door is locked the way we think it is."
/// </summary>
public sealed class KnetPaymentEndpointTests : IClassFixture<InMemoryAppFactory>
{
    private readonly InMemoryAppFactory _factory;
    public KnetPaymentEndpointTests(InMemoryAppFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    [Fact]
    public async Task Response_endpoint_accepts_anonymous_callbacks()
    {
        // K-NET POSTs from their gateway — they can't carry our auth cookie
        // or anti-forgery token. The endpoint MUST be anonymously reachable;
        // the security check lives inside HandleKnetResponseAsync (TrackId
        // lookup), not in the auth pipeline. This test pins that contract:
        // tearing off [AllowAnonymous] would lock real callbacks out.
        using var client = Client();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["trackid"] = "TRK_GHOST",
            ["result"] = "CAPTURED",
            ["paymentid"] = "KP_X",
        });

        var response = await client.PostAsync("/api/payments/knet/response", content);

        // The endpoint must not return 401/403 — those would block the
        // K-NET gateway from delivering callbacks. A redirect (302) to
        // PaymentFailed is the correct rejection for an unknown TrackId.
        ((int)response.StatusCode).Should().BeInRange(200, 399,
            "K-NET cannot present credentials; the endpoint must accept the POST. " +
            "Per-callback authorization lives in HandleKnetResponseAsync.");
    }

    [Fact]
    public async Task Response_endpoint_with_unknown_trackid_redirects_to_failed_page()
    {
        using var client = Client();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["trackid"] = "TRK_DOES_NOT_EXIST_" + Guid.NewGuid(),
            ["result"] = "CAPTURED",
            ["paymentid"] = "KP_FORGED",
            ["tranid"] = "TX_FORGED",
        });

        var response = await client.PostAsync("/api/payments/knet/response", content);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.OriginalString;
        location.Should().Contain("/TenantPortal/PaymentFailed",
            "unknown TrackId is the forged-callback path — must redirect to failure UI");
        location.Should().NotContain("/PaymentSuccess",
            "a forged callback must NEVER land on the success page");
    }

    [Fact]
    public async Task Response_endpoint_with_missing_trackid_redirects_to_failed_page()
    {
        using var client = Client();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["result"] = "CAPTURED",
            // trackid intentionally omitted
        });

        var response = await client.PostAsync("/api/payments/knet/response", content);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/TenantPortal/PaymentFailed");
    }

    [Fact]
    public async Task Initiate_endpoint_rejects_request_without_session_or_explicit_TenantId()
    {
        // Production guard: anonymous initiate with TenantId=0 (the default
        // when nothing is supplied) must NOT create a payment record. The
        // controller returns 401 when neither the session nor a positive
        // TenantId is present — that's the contract this test pins.
        using var client = Client();
        using var content = new StringContent(
            """{ "TenantId": 0, "InvoiceId": 1, "Amount": 100.000 }""",
            System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/payments/knet/initiate", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "without a session and without a positive TenantId in the body, " +
            "the endpoint must reject — anything else would let an unauth caller " +
            "initiate payments against any tenant by enumerating InvoiceId");
    }

    [Fact]
    public async Task Kill_switch_disables_initiate_but_keeps_response_endpoint_alive()
    {
        // Sprint 7 — `Knet:Enabled=false` is the emergency stop. Initiate
        // must 503; response must still accept callbacks so in-flight
        // captures complete cleanly before we go dark.
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Knet:Enabled"] = "false" });
        }));

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // 1. Initiate is gated.
        using var initiateBody = new StringContent(
            """{ "TenantId": 1, "InvoiceId": 1, "Amount": 100.000 }""",
            System.Text.Encoding.UTF8, "application/json");
        var initiateResp = await client.PostAsync("/api/payments/knet/initiate", initiateBody);
        ((int)initiateResp.StatusCode).Should().Be(503,
            "kill switch must produce 503 Service Unavailable so the client UI can show a clear maintenance message");
        var initiateBodyText = await initiateResp.Content.ReadAsStringAsync();
        initiateBodyText.Should().Contain("disabled",
            "the error body must explain why so the tenant doesn't blame themselves");

        // 2. Response endpoint stays open.
        using var responseBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["trackid"] = "TRK_INFLIGHT",
            ["result"] = "CAPTURED",
            ["paymentid"] = "KP_INFLIGHT",
        });
        var responseResp = await client.PostAsync("/api/payments/knet/response", responseBody);
        ((int)responseResp.StatusCode).Should().BeInRange(200, 399,
            "in-flight callbacks must complete even while initiate is killed");
    }
}
