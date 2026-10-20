using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RentCollection.Core.Entities;
using RentCollection.Infrastructure.Data;
using RentCollection.Web.Filters;
using RentCollection.Web.Services;
using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using RentCollection.Web;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog reading from appsettings ("Serilog" section). Falls back to
// Console + RequestId enrichment so logs are useful out of the box.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {RequestId} {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// Sentry error monitoring. DSN is loaded from configuration (Sentry:Dsn) —
// in dev set it via:
//   dotnet user-secrets set "Sentry:Dsn" "https://...@sentry.io/..."
// If the DSN is empty the SDK is a no-op, so this is safe to leave wired up.
builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"];
    options.Environment = builder.Environment.EnvironmentName;
    options.SendDefaultPii = false;          // never send headers/cookies/usernames
    options.MaxBreadcrumbs = 50;
    options.AttachStacktrace = true;
    // Trace 20% of requests in prod; everything in dev for visibility.
    options.TracesSampleRate = builder.Environment.IsDevelopment() ? 1.0 : 0.2;
    // Redact JWT and authorization headers in case any breadcrumb tries to ship them.
    options.SetBeforeSend(@event =>
    {
        if (@event.Request?.Headers != null)
        {
            foreach (var k in @event.Request.Headers.Keys.ToList())
            {
                if (k.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    @event.Request.Headers[k] = "[redacted]";
                }
            }
        }
        return @event;
    });
});

// OpenTelemetry — traces + metrics (Sprint 3 A4). Exporter is OTLP, which
// any collector (Grafana Tempo/Prometheus, Honeycomb, Aspire dashboard,
// AppInsights via the OTel collector) understands. When OTel:Endpoint is
// blank the OTLP exporter no-ops, so this is safe to leave wired.
//
// Configure in appsettings:
//   "OpenTelemetry": {
//     "ServiceName": "RentCollection.Web",
//     "Endpoint": "http://localhost:4317"
//   }
var otelServiceName = builder.Configuration["OpenTelemetry:ServiceName"]
    ?? "RentCollection.Web";
var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName: otelServiceName, serviceVersion: "1.0.0")
        .AddAttributes(new KeyValuePair<string, object>[]
        {
            new("deployment.environment", builder.Environment.EnvironmentName),
        }))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation(opt =>
            {
                // Skip the noise from health-check probes + the OTel collector
                // exporter itself (would be recursive otherwise).
                opt.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health") &&
                    !ctx.Request.Path.StartsWithSegments("/healthz");
            })
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation(opt =>
            {
                // Capture exceptions as span events; statement text is omitted
                // by default (PII risk). Enable per-environment via the
                // SqlClient instrumentation options if you need full SQL in dev.
                opt.RecordException = true;
            });
        if (!string.IsNullOrWhiteSpace(otelEndpoint))
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otelEndpoint))
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint));
    });

// Add services to the container.
// ── Localization (English + Arabic/Kuwait). Resources live under /Resources;
// views use @inject IViewLocalizer. ar-KW is configured with Western digits
// (DigitShapes.None) per the Kuwait convention for money/dates. ──────────────
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(o =>
{
    var en = new System.Globalization.CultureInfo("en-US");
    var ar = new System.Globalization.CultureInfo("ar-KW");
    // Force Western (Latin) digits in Arabic so KWD amounts read 300.000, not ٣٠٠.
    ar.NumberFormat.DigitSubstitution = System.Globalization.DigitShapes.None;
    ar.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();

    var supported = new[] { en, ar };
    o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(en);
    o.SupportedCultures = supported;
    o.SupportedUICultures = supported;
    // Cookie first (explicit user choice), then Accept-Language header.
    o.RequestCultureProviders = new List<Microsoft.AspNetCore.Localization.IRequestCultureProvider>
    {
        // Query string first so the KNET success/failure redirect can force the
        // payer's app language (e.g. /TenantPortal/PaymentSuccess?culture=ar).
        new Microsoft.AspNetCore.Localization.QueryStringRequestCultureProvider(),
        new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider(),
        new Microsoft.AspNetCore.Localization.AcceptLanguageHeaderRequestCultureProvider(),
    };
});

builder.Services.AddControllersWithViews(options =>
{
    // Require authentication by default for all controllers/actions
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));

    // Sprint 12 — stop MVC from treating C# non-nullable reference types
    // (`null!` initialisers on EF navigation properties) as implicit
    // [Required]. Without this, every form that POSTs an entity with
    // `public Tenant Tenant { get; set; } = null!;` etc. silently fails
    // ModelState validation because the navigation isn't bound from the
    // form — the controller bails out of `if (ModelState.IsValid)` and
    // re-renders the page, making the user think "I submitted but nothing
    // happened." (Reported by user: created lease not appearing.)
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

// SharedResource.ar.resx carries ~70 key pairs that differ only by case
// ("Active"/"active"); the compiled store keeps one, so the other fell back
// to English. This wrapper retries a miss with the other casings. Registered
// AFTER AddViewLocalization so it wins the IHtmlLocalizer<T> resolution.
builder.Services.AddTransient(typeof(IHtmlLocalizer<>), typeof(RentCollection.Web.Services.CaseInsensitiveHtmlLocalizer<>));

// Add Database Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
// LEASE history Phase C — register the Lease-edit audit interceptor before
// the DbContext so it's available to be attached below. Scoped so it shares
// the HttpContext of the request that triggered the save.
builder.Services.AddScoped<
    RentCollection.Infrastructure.Services.Audit.LeaseAuditInterceptor>();
// Multi-entity audit interceptor (Tenant/Property/Unit). Same shape as the
// Lease interceptor but covers the other domain entities so the AuditLog
// viewer at /Audit/{type}/{id} sees edits across the system.
builder.Services.AddScoped<
    RentCollection.Infrastructure.Services.Audit.EntityAuditInterceptor>();

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    // Remote shared SQL (SmarterASP) can stall/throttle. Without these, a blocked
    // query has NO command timeout and hangs the request indefinitely (observed:
    // /Tenants, /Properties, / never returning). EnableRetryOnFailure rides out
    // transient blips; CommandTimeout(30) converts an indefinite hang into a fast,
    // catch-able failure so the page shows an error instead of spinning forever.
    options.UseSqlServer(connectionString, sql =>
    {
        // NOTE: EnableRetryOnFailure was removed — its SqlServerRetryingExecutionStrategy
        // is incompatible with user-initiated (explicit) transactions, and KNET payment
        // initiation wraps its work in one, throwing at runtime. CommandTimeout still
        // makes a stalled query fail fast instead of hanging indefinitely.
        sql.CommandTimeout(30);
    });
    options.AddInterceptors(
        sp.GetRequiredService<RentCollection.Infrastructure.Services.Audit.LeaseAuditInterceptor>(),
        sp.GetRequiredService<RentCollection.Infrastructure.Services.Audit.EntityAuditInterceptor>());
});

// Demo deployments without TLS (e.g. SmarterASP ktempurl URLs) need cookies
// sent over plain HTTP. Demo:LoginEnabled=true relaxes Secure from Always to
// SameAsRequest for BOTH the auth cookie and the tenant session cookie.
var demoLoginEnabled = builder.Configuration.GetValue<bool>("Demo:LoginEnabled");

// Add Session support for Tenant Portal
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "TenantSession";
    // SECURITY: the session IS the tenant's identity. Secure unless the HTTP
    // demo is on; Lax (not Strict) so the KNET gateway's top-level redirect
    // back to /TenantPortal/PaymentSuccess still carries the session.
    options.Cookie.SecurePolicy = demoLoginEnabled
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Register authorization filters
builder.Services.AddScoped<TenantAuthorizationFilter>();
builder.Services.AddScoped<AdminAuthorizationFilter>();

// Company-scope context — backs EF Core HasQueryFilter on Property/Tenant/User
// so a logged-in user can only see rows from their own company.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RentCollection.Application.Interfaces.ICompanyContext,
    RentCollection.Web.Services.HttpCompanyContext>();

// Register JWT Token Service
builder.Services.AddScoped<RentCollection.Application.Interfaces.IJwtTokenService, JwtTokenService>();

// Register Payment Services (Cash & Bank Transfer Module)
builder.Services.AddScoped(typeof(RentCollection.Application.Interfaces.IRepository<>),
    typeof(RentCollection.Infrastructure.Repositories.Repository<>));
builder.Services.AddScoped<RentCollection.Application.Interfaces.IPaymentService,
    RentCollection.Application.Services.PaymentService>();
builder.Services.AddScoped<RentCollection.Application.Interfaces.ICashPaymentService,
    RentCollection.Application.Services.CashPaymentService>();
builder.Services.AddScoped<RentCollection.Application.Interfaces.IBankTransferService,
    RentCollection.Application.Services.BankTransferService>();

// Register File Storage Service
builder.Services.AddScoped<RentCollection.Application.Interfaces.IFileStorageService>(provider =>
{
    var webHostEnvironment = provider.GetRequiredService<IWebHostEnvironment>();
    var uploadPath = Path.Combine(webHostEnvironment.WebRootPath, "uploads");
    if (!Directory.Exists(uploadPath))
    {
        Directory.CreateDirectory(uploadPath);
    }
    return new RentCollection.Infrastructure.Services.FileStorageService(uploadPath);
});

// Email + Notification outbox.
// Sprint 25 — SMTP config now lives in the DB (EmailSettings table) so
// admins can change provider via the Email Settings page without re-deploy.
// The IOptions<SmtpEmailSettings> binding was removed; SmtpEmailService now
// reads from IEmailSettingsService on every send.
builder.Services.AddScoped<RentCollection.Application.Interfaces.IEmailSettingsService,
    RentCollection.Infrastructure.Services.Email.EmailSettingsService>();
builder.Services.AddScoped<RentCollection.Application.Interfaces.IEmailService,
    RentCollection.Infrastructure.Services.Email.SmtpEmailService>();

// Sprint 26 — Firebase Cloud Messaging (push notifications). Config from
// appsettings.json "Fcm" section. Until ServerKey is set + Enabled=true,
// every push send is a no-op DryRun (logged, treated as success). Drop in
// the Server Key from Firebase Console -> Project Settings -> Cloud Messaging
// to start sending real pushes. See docs/PUSH_NOTIFICATIONS_SETUP.md.
builder.Services.Configure<RentCollection.Infrastructure.Services.Fcm.FcmSettings>(
    builder.Configuration.GetSection("Fcm"));
builder.Services.AddHttpClient<RentCollection.Application.Interfaces.IFcmService,
    RentCollection.Infrastructure.Services.Fcm.FcmService>();

builder.Services.AddScoped<RentCollection.Application.Interfaces.INotificationService,
    RentCollection.Infrastructure.Services.NotificationService>();

// PDF generator (Item 6). QuestPDF community licence — free for revenue <$1M USD.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Invoice branding (logo, colours, bank details, footer terms). Single-tenant
// — every PDF rendered uses these values. Multi-company branding is a
// deferred follow-up; if you need it, promote these fields to the Company
// entity.
builder.Services.Configure<RentCollection.Application.Branding.BrandingSettings>(
    builder.Configuration.GetSection("Branding"));

// Resolve LogoPath against the web root at startup so the Infrastructure
// renderer doesn't need a hosting dependency. Idempotent: only rewrites
// when the configured path is relative.
builder.Services.PostConfigure<RentCollection.Application.Branding.BrandingSettings>(b =>
{
    if (!string.IsNullOrWhiteSpace(b.LogoPath) && !Path.IsPathRooted(b.LogoPath))
    {
        // builder.Environment is available at this point in the pipeline
        // (we're still configuring services).
        b.LogoPath = Path.Combine(builder.Environment.WebRootPath, b.LogoPath);
    }
});

// Effective branding = appsettings baseline + the BrandingProfiles row saved
// on /CompanySettings. Loaded once at startup (bootstrap below) and refreshed
// on save. Factory registration avoids ctor ambiguity in QuestPdfService.
builder.Services.AddSingleton<RentCollection.Application.Branding.BrandingCache>();
builder.Services.AddScoped<RentCollection.Application.Interfaces.IPdfService>(sp =>
    new RentCollection.Infrastructure.Services.Pdf.QuestPdfService(
        sp.GetRequiredService<RentCollection.Application.Branding.BrandingCache>()));

// Audit log writer (LEASE_AUDIT Flaw #16). Scoped so it shares the
// DbContext + transaction with the action it's recording.
builder.Services.AddScoped<RentCollection.Application.Interfaces.IAuditLogService,
    RentCollection.Infrastructure.Services.Audit.DbAuditLogService>();

// Tenant credential issuer — generates + emails temp password on first
// onboarding and on admin "Resend login". Scoped because it uses the
// request-scoped DbContext.
builder.Services.AddScoped<RentCollection.Application.Interfaces.ITenantCredentialService,
    RentCollection.Infrastructure.Services.TenantCredentialService>();

// Executive dashboard composer — shared between ReportsController (PDF
// download + JSON snapshot) and the monthly Hangfire email job (Sprint 4).
builder.Services.AddScoped<RentCollection.Application.Interfaces.IExecutiveDashboardComposer,
    RentCollection.Infrastructure.Services.ExecutiveDashboardComposer>();

// Test-seam clock (Sprint 1 reliability work) — production wiring uses
// SystemClock; tests inject a FixedClock to pin "now".
builder.Services.AddSingleton<RentCollection.Application.Interfaces.IClock,
    RentCollection.Application.Interfaces.SystemClock>();

// Month-end close aggregator — extracted from ReportsController so the
// 9-step logic is unit-testable without MVC + auth + DI plumbing.
builder.Services.AddScoped<RentCollection.Application.Interfaces.IMonthEndCloseService,
    RentCollection.Infrastructure.Services.MonthEndCloseService>();

// Civil ID OCR enrichment (Sprint 21) — Gemini Vision backed.
// API key is read from user-secrets (Gemini:ApiKey) in dev and the
// Gemini__ApiKey env var in prod. NEVER commit the key.
builder.Services.Configure<RentCollection.Infrastructure.Services.Gemini.GeminiSettings>(
    builder.Configuration.GetSection("Gemini"));
builder.Services.AddMemoryCache();
// SECURITY (audit H2): per-account brute-force lockout for the login endpoints.
builder.Services.AddSingleton<RentCollection.Web.Services.ILoginThrottle,
    RentCollection.Web.Services.LoginThrottle>();
// Admin-managed dropdown values (bilingual), consumed by dropdowns across the app.
builder.Services.AddScoped<RentCollection.Web.Services.ILookupService,
    RentCollection.Web.Services.LookupService>();
builder.Services.AddHttpClient<
    RentCollection.Application.Interfaces.ICivilIdEnrichmentService,
    RentCollection.Infrastructure.Services.Gemini.GeminiCivilIdService>();


// Response caching — backs the [ResponseCache] attrs on the Reports
// snapshot + PDF endpoints (Sprint 5). 30 s on JSON / 60 s on PDF.
builder.Services.AddResponseCaching();

// Document storage abstraction (Item 11) — local impl today, S3/Blob later.
builder.Services.AddScoped<RentCollection.Application.Interfaces.IDocumentStore,
    RentCollection.Infrastructure.Services.LocalDocumentStore>();

// MFA / TOTP (Item 12) — singleton, stateless wrapper over OtpNet.
builder.Services.AddSingleton<RentCollection.Web.Services.TotpService>();

// Register KNET Payment Service (Direct KNET Gateway)
builder.Services.Configure<RentCollection.Infrastructure.Services.KnetSettings>(
    builder.Configuration.GetSection("Knet"));
builder.Services.AddScoped<RentCollection.Infrastructure.Services.KnetUtil>();
builder.Services.AddScoped<RentCollection.Application.Interfaces.IKnetPaymentService,
    RentCollection.Infrastructure.Services.KnetPaymentService>();

// Sprint 24 — enforce oldest-unpaid-first payment ordering across every entry point.
builder.Services.AddScoped<RentCollection.Application.Interfaces.IPaymentEligibilityService,
    RentCollection.Infrastructure.Services.PaymentEligibilityService>();

// Register Tenant Lease Service (Multi-lease Civil ID support)
builder.Services.AddScoped<RentCollection.Application.Interfaces.ITenantLeaseService,
    RentCollection.Infrastructure.Services.TenantLeaseService>();

// Register Payment Verification Service (Admin mismatch detection & reconciliation)
builder.Services.AddScoped<RentCollection.Application.Interfaces.IPaymentVerificationService,
    RentCollection.Infrastructure.Services.PaymentVerificationService>();

// Configure file upload limits
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5 * 1024 * 1024; // 5MB
});

// Configure Authentication (Cookie for MVC + JWT for API)
// Persist DataProtection keys to disk. On SmarterASP shared hosting the app pool
// recycles frequently and every redeploy restarts the app; with in-memory keys
// (the default) each restart regenerates the key ring, which silently invalidates
// ALL existing auth cookies and antiforgery tokens. That surfaces as "login stops
// working after a deploy" and login->logout loops. Persisting + pinning the app
// name keeps the same keys across restarts.
var dpKeysDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dpkeys");
Directory.CreateDirectory(dpKeysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysDir))
    .SetApplicationName("RentCollection");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.Name = "RentCollectionAuth";
    options.Cookie.HttpOnly = true;
    // Demo deployments without TLS (e.g. SmarterASP ktempurl URLs) need the
    // cookie sent over plain HTTP. Setting Demo:LoginEnabled=true relaxes
    // this from Always to SameAsRequest so the auth cookie survives.
    options.Cookie.SecurePolicy = demoLoginEnabled
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // SECURITY: server-side revocation for the 12-hour sliding cookie. A
    // deactivated/deleted/demoted user otherwise keeps working until the cookie
    // expires. Re-check the principal against Users at most once per 5 minutes
    // per user (IMemoryCache marker keyed by id+role) so this costs one cheap
    // query per user per window, not one per request. Fails OPEN on
    // infrastructure errors, same as the JWT OnTokenValidated hook above.
    options.Events.OnValidatePrincipal = async ctx =>
    {
        var idRaw = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var role = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
        if (!int.TryParse(idRaw, out var userId)) return;

        var cache = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var cacheKey = $"auth-principal-ok:{userId}:{role}";
        if (cache.TryGetValue(cacheKey, out _)) return;

        bool stillValid;
        try
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            // IgnoreQueryFilters: HttpContext.User isn't populated yet at this
            // point, so the company filter can't be relied on either way.
            var u = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => new { x.IsActive, x.Role })
                .FirstOrDefaultAsync();

            stillValid = u != null && u.IsActive && string.Equals(u.Role, role, StringComparison.Ordinal);
        }
        catch
        {
            return; // Infrastructure error — do NOT fail closed (would log everyone out).
        }

        if (stillValid)
        {
            cache.Set(cacheKey, true, TimeSpan.FromMinutes(5));
            return;
        }

        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    };
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var secretKey = jwtSettings["Key"];
    if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32)
    {
        throw new InvalidOperationException(
            "Jwt:Key must be configured (>= 32 chars). Set via user-secrets or environment variables — never commit it.");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        // SECURITY (audit L1): pin the algorithm so a token can't be presented
        // under a different (or "none") algorithm.
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    // SECURITY (audit M2): server-side revocation. On every validated token,
    // confirm the tenant still exists, is Active and not soft-deleted, and that
    // the token's security stamp still matches the current password hash — so a
    // deactivated/deleted tenant or a password change immediately invalidates
    // outstanding access tokens (previously valid for the full 60-min TTL).
    // Fails OPEN on infrastructure errors (DB blip) so a transient fault can't
    // lock every tenant out of the app.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            try
            {
                var tid = ctx.Principal?.FindFirst("tenantId")?.Value;
                if (!int.TryParse(tid, out var tenantId)) { ctx.Fail("Invalid token subject"); return; }

                var db = ctx.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var t = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tenantId)
                    .Select(x => new { x.Status, x.IsDeleted, x.Password })
                    .FirstOrDefaultAsync();

                if (t == null || t.IsDeleted || t.Status != "Active")
                {
                    ctx.Fail("Tenant is no longer active");
                    return;
                }

                var stmp = ctx.Principal?.FindFirst("stmp")?.Value;
                if (!string.IsNullOrEmpty(stmp) &&
                    !string.Equals(stmp,
                        RentCollection.Web.Services.JwtTokenService.ComputeSecurityStamp(t.Password),
                        StringComparison.Ordinal))
                {
                    ctx.Fail("Token has been revoked");
                }
            }
            catch
            {
                // Infrastructure error — do NOT fail closed (would log everyone out).
            }
        }
    };
});

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Rent Collection API",
        Version = "v1",
        Description = "Rent Collection System API"
    });

    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Rate limiting (Sprint 4 A5). One policy for /tenant/refresh — abusing
// the endpoint is a known attack on session-based JWT renewal, so we cap
// it at 6 attempts per minute per IP. Other endpoints follow the global
// fallback (no limit by default; tighten later as needed).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("refresh", o =>
    {
        o.PermitLimit = 6;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
    // Sprint 25 — anti-abuse cap on the Email Settings Test button.
    // 3 sends per minute per IP is plenty for a real admin testing creds
    // and tight enough to block someone using it to fire arbitrary mail
    // from the corporate domain.
    options.AddFixedWindowLimiter("email-test", o =>
    {
        o.PermitLimit = 3;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });

    // SECURITY: per-IP caps on the two anonymous/high-value tenant endpoints —
    // credential stuffing on /api/tenant/login and payment-record spam on
    // /api/payments/knet/initiate. Applied via the GlobalLimiter, partitioned
    // by path+IP, so no [EnableRateLimiting] attribute is needed on the
    // controllers; every other path gets a no-op partition. The global limiter
    // runs alongside the named policies above, not instead of them.
    // RemoteIpAddress is null only on the in-memory TestServer transport
    // (WebApplicationFactory), where we skip limiting so the integration suite
    // isn't throttled by a shared "unknown" bucket; IIS/Kestrel always set it.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path;
        var ip = httpContext.Connection.RemoteIpAddress;
        if (ip != null &&
            (path.StartsWithSegments("/api/tenant/login", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWithSegments("/api/payments/knet/initiate", StringComparison.OrdinalIgnoreCase)))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                $"{path.Value!.ToLowerInvariant()}|{ip}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        }
        return RateLimitPartition.GetNoLimiter("unlimited");
    });
});

// Add CORS. SECURITY (audit H10): this policy sets AllowCredentials(), so the
// origin allow-list must be tight. The native mobile app does NOT use browser
// CORS, so production only needs the web domain(s). Origins come from config
// (Cors:AllowedOrigins); in production we fall back to the live host, and the
// any-localhost allowance is enabled ONLY in Development (previously any
// localhost:port on the shared host was a trusted, credentialed origin).
var isDev = builder.Environment.IsDevelopment();
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? new[]
        {
            "https://www.alrtam.co",
            "https://alrtam.co"
        };

    options.AddPolicy("FlutterApp", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            {
                // Localhost (any port) is trusted ONLY in Development so
                // `flutter run -d chrome` works; never in production.
                if (isDev && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    && (uri.Host == "localhost" || uri.Host == "127.0.0.1"))
                    return true;
                return allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Health checks — split into Kubernetes-style probes (Sprint 5 A7):
//   /health/live  — process is alive (no dependencies). Failing → orchestrator restarts the pod.
//   /health/ready — dependencies OK (database). Failing → orchestrator stops sending traffic but does NOT restart.
//   /health       — legacy alias for /health/live.
// Tags drive the filter: only checks tagged "ready" run on /health/ready.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(
        name: "database",
        tags: new[] { "ready", "db" })
    // Sprint 1 reliability work — flag a stale scheduler before the rent
    // job has been silently un-running for days.
    .AddCheck<RentCollection.Web.Health.HangfireHealthCheck>(
        name: "hangfire",
        tags: new[] { "ready", "scheduler" });

// FluentValidation — auto-discovers AbstractValidator<T> in the Application assembly.
builder.Services.AddValidatorsFromAssemblyContaining<RentCollection.Application.Validators.KnetPaymentRequestValidator>();

// Item 4: Hangfire scheduler. Storage = same SQL Server as app data — fine for
// a single-instance deploy. For horizontal scaling, point at a dedicated DB.
// Set `Hangfire:Enabled` to "false" in tests / health-only deploys so we
// don't try to initialise SQL storage against an InMemory provider.
var hangfireEnabled = builder.Configuration.GetValue("Hangfire:Enabled", true);
if (hangfireEnabled)
{
    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"),
            new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                // Was TimeSpan.Zero (poll as fast as possible), which on shared
                // SmarterASP SQL constantly hammers the same DB the web requests use
                // and worsens contention/throttling. 15s is plenty for this app.
                QueuePollInterval = TimeSpan.FromSeconds(15),
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true
            }));
    builder.Services.AddHangfireServer();

    // Register the failure-alert filter globally so it fires for every job
    // (recurring + one-off) without per-job decoration. Resolves IEmailService
    // via the request-scoped activator at fire time.
    Hangfire.GlobalJobFilters.Filters.Add(
        new RentCollection.Web.Services.HangfireFailureAlertFilter());
}
builder.Services.AddScoped<RentCollection.Infrastructure.Services.RecurringJobs>();

// Global exception handler — emits JSON ProblemDetails for API requests and falls
// through to the MVC error page otherwise.
builder.Services.AddExceptionHandler<RentCollection.Web.Middleware.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Sprint 25 — one-time idempotent bootstrap of the EmailSettings table.
// We can't ship a migration here because the deployed SmarterASP DB
// isn't EF-migration-managed (it was restored from a .bak with its own
// __EFMigrationsHistory). This SQL is conservative: IF NOT EXISTS, then
// CREATE — runs on every startup but is a no-op after the first.
try
{
    using var bootstrapScope = app.Services.CreateScope();
    var db = bootstrapScope.ServiceProvider
        .GetRequiredService<RentCollection.Infrastructure.Data.ApplicationDbContext>();
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmailSettings')
        BEGIN
            CREATE TABLE [EmailSettings] (
                [EmailSettingsId] INT IDENTITY(1,1) PRIMARY KEY,
                [Host] NVARCHAR(255) NOT NULL CONSTRAINT [DF_EmailSettings_Host] DEFAULT (''),
                [Port] INT NOT NULL CONSTRAINT [DF_EmailSettings_Port] DEFAULT (587),
                [EnableSsl] BIT NOT NULL CONSTRAINT [DF_EmailSettings_EnableSsl] DEFAULT (1),
                [Username] NVARCHAR(255) NOT NULL CONSTRAINT [DF_EmailSettings_Username] DEFAULT (''),
                [Password] NVARCHAR(500) NOT NULL CONSTRAINT [DF_EmailSettings_Password] DEFAULT (''),
                [FromAddress] NVARCHAR(255) NOT NULL CONSTRAINT [DF_EmailSettings_FromAddress] DEFAULT (''),
                [FromName] NVARCHAR(255) NOT NULL CONSTRAINT [DF_EmailSettings_FromName] DEFAULT ('ALRTAM Company'),
                [DryRun] BIT NOT NULL CONSTRAINT [DF_EmailSettings_DryRun] DEFAULT (1),
                [IsActive] BIT NOT NULL CONSTRAINT [DF_EmailSettings_IsActive] DEFAULT (1),
                [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_EmailSettings_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                [ModifiedAt] DATETIME2 NULL,
                [LastTestAt] DATETIME2 NULL,
                [LastTestResult] NVARCHAR(500) NULL
            );
        END
    ");

    // Sprint 26 — FCM device tokens per tenant.
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TenantDeviceTokens')
        BEGIN
            CREATE TABLE [TenantDeviceTokens] (
                [TenantDeviceTokenId] INT IDENTITY(1,1) PRIMARY KEY,
                [TenantId] INT NOT NULL,
                [Token] NVARCHAR(500) NOT NULL CONSTRAINT [DF_TenantDeviceTokens_Token] DEFAULT (''),
                [Platform] NVARCHAR(20) NOT NULL CONSTRAINT [DF_TenantDeviceTokens_Platform] DEFAULT (''),
                [DeviceId] NVARCHAR(128) NULL,
                [Locale] NVARCHAR(10) NULL,
                [IsActive] BIT NOT NULL CONSTRAINT [DF_TenantDeviceTokens_IsActive] DEFAULT (1),
                [RegisteredAt] DATETIME2 NOT NULL CONSTRAINT [DF_TenantDeviceTokens_RegisteredAt] DEFAULT (SYSUTCDATETIME()),
                [LastSeenAt] DATETIME2 NULL,
                [DeactivatedAt] DATETIME2 NULL,
                [DeactivationReason] NVARCHAR(200) NULL
            );
            CREATE INDEX [IX_TenantDeviceTokens_TenantId_IsActive]
                ON [TenantDeviceTokens] ([TenantId], [IsActive]);
        END
    ");

    // 2026-10-14 — Company & bank details for PDFs (/CompanySettings) and the
    // hand-written receipt-book number on cash payments.
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BrandingProfiles')
        BEGIN
            CREATE TABLE [BrandingProfiles] (
                [BrandingProfileId] INT IDENTITY(1,1) PRIMARY KEY,
                [CompanyName] NVARCHAR(200) NULL,
                [TagLine] NVARCHAR(200) NULL,
                [ContactAddress] NVARCHAR(300) NULL,
                [ContactPhone] NVARCHAR(50) NULL,
                [ContactEmail] NVARCHAR(200) NULL,
                [Website] NVARCHAR(200) NULL,
                [BankName] NVARCHAR(200) NULL,
                [BankAccountHolder] NVARCHAR(200) NULL,
                [BankIban] NVARCHAR(50) NULL,
                [BankSwiftBic] NVARCHAR(20) NULL,
                [FooterTerms] NVARCHAR(1000) NULL,
                [FooterTermsAr] NVARCHAR(1000) NULL,
                [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_BrandingProfiles_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                [ModifiedAt] DATETIME2 NULL,
                [ModifiedBy] NVARCHAR(100) NULL
            );
        END
        IF COL_LENGTH('CashPayments', 'ManualReceiptNumber') IS NULL
            ALTER TABLE [CashPayments] ADD [ManualReceiptNumber] NVARCHAR(50) NULL;
    ");

    var brandingRow = await db.BrandingProfiles.AsNoTracking()
        .OrderByDescending(b => b.BrandingProfileId).FirstOrDefaultAsync();
    app.Services.GetRequiredService<RentCollection.Application.Branding.BrandingCache>().Apply(brandingRow);
}
catch (Exception ex)
{
    // Don't crash startup if bootstrap fails (e.g. read-only DB) — the
    // EmailSettings page will surface a friendly error and the rest of
    // the app works without it (SmtpEmailService defaults to DryRun).
    Log.Warning(ex, "EmailSettings table bootstrap failed; admin page may be unavailable until table exists.");
}

// Hand the Hangfire failure-alert filter the root service provider so it
// can build its own scope when a job fails — filter callbacks run outside
// any request scope.
RentCollection.Web.Services.HangfireFailureAlertFilter.RootServices = app.Services;

// Serilog request logging — every request gets a one-liner with method, status, elapsed.
app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Audit L4: give the handler a fallback path so unhandled MVC (browser)
    // errors render the friendly /Home/Error page instead of a bare 500. The
    // GlobalExceptionHandler still intercepts /api requests first (returns JSON).
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// SECURITY (audit H9): Swagger exposed the full API surface (auth, KNET payment,
// tenant portal) on the live host. Serve it only in Development, or when the
// explicit Swagger:Enabled flag is set (off by default in production).
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Rent Collection API V1");
        c.RoutePrefix = "swagger";
    });
}

// SECURITY (audit H8): baseline security response headers (no WAF on shared
// hosting to add these at the edge). CSP is intentionally deferred — it needs
// per-view testing against the admin UI's inline scripts/styles before enforcing.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";              // clickjacking (allow same-origin framing)
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=(), payment=()";
    // SECURITY (audit H8): Content-Security-Policy. This is the "safe" phase-1
    // policy from the view analysis — it allows the inline scripts/handlers/styles
    // the admin UI currently depends on (254 inline handlers + ~80 style blocks
    // make a nonce-only policy a multi-sprint refactor) plus the exact CDN origins
    // in use, while hardening framing, base-uri, form-action and object-src. It
    // still blocks injected external script/frame origins. Hardening script-src to
    // nonces is tracked as follow-up work.
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' https://cdnjs.cloudflare.com https://fonts.gstatic.com data:; " +
        "img-src 'self' data: https://upload.wikimedia.org; " +
        "connect-src 'self'; frame-ancestors 'self'; base-uri 'self'; form-action 'self'; object-src 'none'";
    await next();
});

app.UseHttpsRedirection();

// SECURITY (audit C3/C4): the /uploads tree holds PRIVATE tenant files —
// bank-transfer receipts, national-ID scans, maintenance photos. It sits under
// wwwroot, so UseStaticFiles would otherwise serve it ANONYMOUSLY (unauth IDOR),
// and a malicious .svg/.html upload served from our own origin becomes stored
// XSS. This guard runs BEFORE UseStaticFiles and, for /uploads only:
//   1. 404s script-capable file types so they can never execute from our origin;
//   2. requires an authenticated session (cookie or JWT) — the mobile app never
//      fetches these URLs (it only uploads), so the sole consumers are the
//      authenticated admin web views;
//   3. stamps nosniff so a mislabeled file can't be MIME-sniffed into script.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase))
    {
        var ext = System.IO.Path.GetExtension(context.Request.Path.Value ?? string.Empty).ToLowerInvariant();
        string[] blockedExts = { ".svg", ".svgz", ".html", ".htm", ".xhtml", ".xml", ".js", ".mjs", ".mht", ".mhtml", ".shtml", ".swf" };
        if (Array.IndexOf(blockedExts, ext) >= 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var authed = (await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Succeeded
            || (await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)).Succeeded;
        if (!authed)
        {
            // 404 (not 401) so we don't confirm the file exists to an anonymous caller.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
    await next();
});

app.UseStaticFiles();

// Apply the request culture (cookie → Accept-Language) before routing so
// views/controllers resolve the right language + RTL direction.
app.UseRequestLocalization(app.Services.GetRequiredService<
    Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>>().Value);

app.UseRouting();

app.UseCors("FlutterApp");

app.UseSession();

// Rate limiter goes before auth so abusive unauthenticated traffic to
// /tenant/refresh also gets capped — otherwise the [Authorize] filter
// short-circuits the request with 401 before the limiter sees it.
app.UseRateLimiter();
// ResponseCaching is required by the [ResponseCache(VaryByQueryKeys=...)] attributes
// on the KpiBatch endpoints — without it those endpoints throw 500. It was briefly
// removed while diagnosing a list-page stall, but that stall was a curl/no-gzip
// artifact (browsers send Accept-Encoding and are unaffected), not this middleware.
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();

// Hangfire dashboard + recurring-job seeding — skipped when Hangfire is
// disabled (tests / health-only deploys). Same `Hangfire:Enabled` flag.
if (hangfireEnabled)
{
    // SECURITY: the dashboard exposes every company's job arguments and lets
    // the viewer trigger/delete jobs, so it is SuperAdmin-only. (The shared
    // HangfireAdminAuthorizationFilter also admits per-company Admins.)
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new Hangfire.Dashboard.IDashboardAuthorizationFilter[] { new HangfireSuperAdminAuthorizationFilter() }
    });

// Schedule recurring jobs on first boot. Idempotent — RecurringJobs.AddOrUpdate
// upserts so this can run on every deploy without duplicates.
using (var scope = app.Services.CreateScope())
{
    var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    // Job IDs come from RecurringJobIds so the controller's TriggerJob
    // allow-list, the view JS data-job attributes, and these registrations
    // can never drift apart silently. Renaming a job in one place fails
    // the reflection test in `RecurringJobIdsTests`.
    var ids = RentCollection.Application.Interfaces.RecurringJobIds.All;
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.GenerateMonthlyInvoices,
        j => j.GenerateMonthlyInvoicesAsync(),
        Cron.Monthly(day: 1, hour: 2, minute: 0));
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.FlipOverdueInvoices,
        j => j.FlipOverdueInvoicesAsync(),
        Cron.Daily(hour: 6, minute: 0));
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.LeaseExpiryReminders,
        j => j.LeaseExpiryRemindersAsync(),
        Cron.Daily(hour: 7, minute: 0));
    // LEASE_AUDIT Flaw #7 — daily Active→Expired status flip. Runs at
    // 01:00 UTC (≈ 04:00 Kuwait), before the reminder jobs at 07:00, so
    // the today-expiring lease has already flipped to Expired by the
    // time the morning summary lands.
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.FlipExpiredLeases,
        j => j.FlipExpiredLeasesAsync(),
        Cron.Daily(hour: 1, minute: 0));
    // Reconcile Unit.Status against active leases. Runs AFTER
    // flip-expired-leases so the day's newly-Expired leases are accounted
    // for before unit-status flips Occupied → Vacant.
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.ReconcileUnitStatus,
        j => j.ReconcileUnitStatusAsync(),
        Cron.Daily(hour: 1, minute: 30));
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.ProcessNotificationOutbox,
        j => j.ProcessNotificationOutboxAsync(),
        "*/2 * * * *");  // every 2 minutes
    // Phase 3 — expire abandoned KNET payments (Pending with no callback and no
    // settlement evidence) so reconciliation isn't polluted. Runs daily at 02:15
    // UTC, well after the near-instant gateway callback window.
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.ExpireStaleKnetPayments,
        j => j.ExpireStaleKnetPaymentsAsync(),
        Cron.Daily(hour: 2, minute: 15));
    // Sprint 4 — monthly Executive Dashboard email on the 1st at 06:00 UTC.
    // Recipients: `Reports:MonthlyEmailRecipients` (semicolon-separated)
    // or fall back to all admin-role users. Skipped silently if neither
    // resolves to addresses.
    recurring.AddOrUpdate<RentCollection.Infrastructure.Services.RecurringJobs>(
        RentCollection.Application.Interfaces.RecurringJobIds.MonthlyExecutiveDashboardEmail,
        j => j.SendMonthlyExecutiveDashboardAsync(),
        Cron.Monthly(day: 1, hour: 6, minute: 0));
}
} // hangfireEnabled

// Liveness — fast, no dependencies. Used by orchestrator restart probes.
var liveness = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckJsonWriter.Write,
};
app.MapHealthChecks("/health/live", liveness).AllowAnonymous();
app.MapHealthChecks("/health", liveness).AllowAnonymous(); // legacy alias

// Readiness — runs the checks tagged "ready" (currently: database). Used by
// orchestrator traffic gates — failing = stop sending requests but don't kill.
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckJsonWriter.Write,
}).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration test suite
// (Sprint 4 A8). Top-level statements compile Program as `internal` by
// default; making it `public partial` lets the test host resolve it.
public partial class Program { }

/// <summary>
/// Hangfire dashboard gate: authenticated back-office cookie AND the SuperAdmin
/// role. Lives here (not in Services/) so the dashboard registration above is
/// self-evidently SuperAdmin-only.
/// </summary>
internal sealed class HangfireSuperAdminAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (http?.User?.Identity?.IsAuthenticated != true) return false;
        return http.User.IsInRole(Roles.SuperAdmin);
    }
}
