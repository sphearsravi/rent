using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace RentCollection.E2ETests.Helpers;

/// <summary>
/// Shared plumbing for the workflow-tier E2E tests: lightweight ADO scalar/exec
/// helpers (decoupled from EF entity types) and an authenticated MVC form POST
/// that fetches an antiforgery token from a known form page.
/// </summary>
public static class WorkflowSupport
{
    public static string ConnString =>
        Environment.GetEnvironmentVariable("RC_E2E_DB")
        ?? @"Server=RAVIWST\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=true;TrustServerCertificate=true;";

    public static async Task<T?> Scalar<T>(string sql, params (string, object)[] ps)
    {
        using var c = new SqlConnection(ConnString);
        await c.OpenAsync();
        using var cmd = new SqlCommand(sql, c);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var r = await cmd.ExecuteScalarAsync();
        if (r is null || r is DBNull) return default;
        return (T)Convert.ChangeType(r, typeof(T));
    }

    public static async Task Exec(string sql)
    {
        using var c = new SqlConnection(ConnString);
        await c.OpenAsync();
        using var cmd = new SqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// POST an MVC form action with a valid antiforgery token (pulled from a
    /// GET of <paramref name="tokenPage"/>). Shares the browser context's auth
    /// cookies. Returns the HTTP status.
    /// </summary>
    public static async Task<int> PostForm(IBrowserContext ctx, string action,
        (string, string)[]? fields = null, string tokenPage = "/CashPayment/Record")
    {
        var html = await (await ctx.APIRequest.GetAsync(tokenPage)).TextAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var body = "__RequestVerificationToken=" + Uri.EscapeDataString(token);
        foreach (var (k, v) in fields ?? Array.Empty<(string, string)>())
            body += "&" + k + "=" + Uri.EscapeDataString(v);
        var resp = await ctx.APIRequest.PostAsync(action, new APIRequestContextOptions
        {
            Headers = new Dictionary<string, string> { ["content-type"] = "application/x-www-form-urlencoded" },
            Data = body,
            MaxRedirects = 0,
        });
        return resp.Status;
    }

    /// <summary>Reverse-FK teardown of a workflow tree built by these tests.</summary>
    public static Task TeardownTree(int tenantId, int leaseId, int propertyId) => Exec($@"
        DELETE FROM CashPayments   WHERE PaymentId IN (SELECT PaymentId FROM Payments WHERE TenantId={tenantId});
        DELETE FROM BankTransfers  WHERE PaymentId IN (SELECT PaymentId FROM Payments WHERE TenantId={tenantId});
        DELETE FROM KNETReconciliations WHERE PaymentId IN (SELECT PaymentId FROM Payments WHERE TenantId={tenantId});
        DELETE FROM PaymentAudits  WHERE PaymentId IN (SELECT PaymentId FROM Payments WHERE TenantId={tenantId});
        DELETE FROM PaymentIdempotencies WHERE PaymentId IN (SELECT PaymentId FROM Payments WHERE TenantId={tenantId});
        DELETE FROM Refunds        WHERE PaymentId IN (SELECT PaymentId FROM Payments WHERE TenantId={tenantId});
        DELETE FROM Payments       WHERE TenantId={tenantId};
        DELETE FROM Invoices       WHERE LeaseId={leaseId};
        DELETE FROM Leases         WHERE LeaseId={leaseId};
        DELETE FROM Notifications        WHERE TenantId={tenantId};
        DELETE FROM MaintenanceRequests  WHERE TenantId={tenantId};
        DELETE FROM Tenants        WHERE TenantId={tenantId};
        DELETE FROM Units          WHERE PropertyId={propertyId};
        DELETE FROM Properties     WHERE PropertyId={propertyId};");
}
