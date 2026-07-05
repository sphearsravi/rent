using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RentCollection.Application.Interfaces;

namespace RentCollection.Infrastructure.Services.Fcm;

/// <summary>
/// Strongly-typed binding for the "Fcm" section of appsettings.json.
/// <para>
/// Enabled=false short-circuits all sends (logs only). ServerKey is the
/// FCM legacy server key from the Firebase Console — see
/// docs/PUSH_NOTIFICATIONS_SETUP.md for how to get it.
/// </para>
/// </summary>
public class FcmSettings
{
    public bool Enabled { get; set; } = false;
    public string ServerKey { get; set; } = string.Empty;
    /// <summary>Override for the FCM endpoint. Defaults to the production HTTP API.</summary>
    public string Endpoint { get; set; } = "https://fcm.googleapis.com/fcm/send";
    /// <summary>When true (or when Enabled=false), log only — never call FCM. Default safe.</summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>
/// Sprint 26 — FCM dispatcher using the legacy HTTP API
/// (https://fcm.googleapis.com/fcm/send). The legacy API is the simplest
/// auth model (single server-key string) and is sufficient for the demo.
/// For production-grade auth, switch to HTTP v1 with a service-account
/// JWT — see [FcmService] history.
///
/// Inputs/outputs deliberately match <see cref="IEmailService"/> so the
/// outbox dispatcher can treat all channels symmetrically.
/// </summary>
public class FcmService : IFcmService
{
    private readonly HttpClient _http;
    private readonly FcmSettings _settings;
    private readonly ILogger<FcmService> _logger;

    public FcmService(
        HttpClient http,
        IOptions<FcmSettings> settings,
        ILogger<FcmService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<FcmSendResult> SendAsync(
        string deviceToken,
        string title,
        string body,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            return new FcmSendResult(false, "Device token is empty");
        }

        if (!_settings.Enabled || _settings.DryRun || string.IsNullOrWhiteSpace(_settings.ServerKey))
        {
            // Until the Firebase Server Key is set in appsettings.json,
            // pretend success so the outbox doesn't mark every push as
            // Failed. The notification still moves to Sent — but obviously
            // no real push fires. The logs are how an admin verifies.
            _logger.LogInformation(
                "[DRY-RUN push] Token={Token} Title={Title} BodyLength={BodyLen}",
                Truncate(deviceToken, 12), title, body.Length);
            return new FcmSendResult(true);
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _settings.Endpoint)
            {
                Content = JsonContent.Create(new
                {
                    to = deviceToken,
                    notification = new
                    {
                        title,
                        body,
                        sound = "default",
                    },
                    data = data ?? new Dictionary<string, string>(),
                    priority = "high",
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("key", "=" + _settings.ServerKey);
            // The legacy FCM API expects: Authorization: key=YOUR_SERVER_KEY
            // .NET's AuthenticationHeaderValue inserts a space — workaround:
            request.Headers.Authorization = null;
            request.Headers.TryAddWithoutValidation("Authorization", $"key={_settings.ServerKey}");

            var response = await _http.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "FCM send failed. Status={Status} Body={Body}",
                    (int)response.StatusCode, Truncate(responseBody, 500));
                return new FcmSendResult(false, $"FCM HTTP {(int)response.StatusCode}: {Truncate(responseBody, 200)}");
            }

            // FCM legacy API returns 200 even on per-token failures —
            // results live in the JSON body. Look for NotRegistered /
            // InvalidRegistration so the caller can deactivate the token.
            if (responseBody.Contains("NotRegistered", StringComparison.OrdinalIgnoreCase) ||
                responseBody.Contains("InvalidRegistration", StringComparison.OrdinalIgnoreCase))
            {
                return new FcmSendResult(false, "Device token is no longer valid", TokenInvalid: true);
            }

            return new FcmSendResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM send threw. Token={Token}", Truncate(deviceToken, 12));
            return new FcmSendResult(false, ex.Message);
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
