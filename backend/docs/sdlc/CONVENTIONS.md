# RentCollection (Alrtam) SDLC — shared conventions (every agent reads this first)

This file is the single source of truth for BOTH working folders:

| Folder | What | Git |
|---|---|---|
| `C:\Users\r.shankar\RentCollectionSystem` | .NET 8 backend + Razor MVC admin web + tenant portal + mobile REST API | local repo, **no remote**, branch `main` |
| `C:\Users\r.shankar\RentCollectionTenantFlutter` | "Alrtam" tenant mobile app (Flutter, Provider MVVM, Dio, EN+AR/RTL) | private remote `github.com/sphearsravi/alrtam-tenant`, branch `main` |

The Flutter folder's agents point here by absolute path. Do not copy this file there.

## Hand-off folder
Each feature gets **`C:\Users\r.shankar\RentCollectionSystem\docs\sdlc\<feature-slug>\`**. This applies even to
mobile-only features started from the Flutter folder, so there is one ledger. Phases write, in order:

| File | Written by | Read by |
|---|---|---|
| `01-requirements.md` | analyst | everyone |
| `02-design.md` (incl. API + view-model contract, file ownership) | architect | devs, qa, security |
| `03-backend-notes.md` | backend-dev | web-dev, mobile-dev, qa |
| `04-web-notes.md` | web-dev | qa |
| `04-mobile-notes.md` | mobile-dev | qa |
| `05-test-report.md` | qa-tester | devs (bug loop), security |
| `06-review.md` | security-reviewer | devs, devops |
| `07-deploy-log.md` | devops | tech-writer |
| `08-release-notes.md` | tech-writer | user |

Never overwrite another phase's file. If you must add to it, append a dated "Update" section.
Parallel agents must own disjoint files. List the files you own at the top of your notes file.

## Stack — backend/web (`RentCollectionSystem`)
- Solution `RentCollectionSystem.sln`, net8.0, EF Core 9, SQL Server, Hangfire, QuestPDF, Serilog, Sentry.
  - `RentCollection.Core`: entities, enums, `Leases/`
  - `RentCollection.Application`: DTOs, interfaces, services, validators, `Branding/`, `Security/` (incl. the custom `PasswordHasher`)
  - `RentCollection.Infrastructure`: `Data/ApplicationDbContext.cs`, `Migrations/`, `Services/` (KNET, PDF, email…)
  - `RentCollection.Web`:
    - MVC `Controllers/*.cs` + `Views/**`
    - REST for the mobile app in `Controllers/Api/*.cs` (`/api/tenant/...`, `/api/payments/...`)
    - `Program.cs`, `Resources/SharedResource(.ar).resx`, `wwwroot/{css,js}`
  - Tests:
    - `RentCollection.Tests`: xUnit + Moq + EF in-memory. It references Web, so controller tests live in `Tests/Payments`.
    - `RentCollection.IntegrationTests`
    - `RentCollection.E2ETests`: Playwright for .NET
  - `RentCollection.OpsCli`
- **Auth:** staff use a **custom `Users` table**, not ASP.NET Identity.
  - Hashes have the form `v2:600000:<saltB64>:<hashB64>`: PBKDF2-SHA256, 16-byte salt, 32-byte key.
  - Tenants (app and portal) log in with Civil ID + password and get a JWT.
  - In prod the JWT signing key is an **App Pool env var**. Never hand-mint tokens, because they 401.
- **Company scoping:** only Property/Tenant/User/LegalCase have company query filters.
  - Scope everything else via `x.Tenant.CompanyId` or `u.Property.CompanyId`.
  - Controllers use `ScopedCompanyId()`: null means SuperAdmin, -1 means no claim, so deny.
  - A cross-company leak is P0.
- **Money:** KWD has 3 decimals.
  - Use `decimal(18,3)` and literals like `100.500m`.
  - Format with `ToString("N3", CultureInfo.InvariantCulture)`. In JS use `toLocaleString("en-US")`.
  - Keep Western digits even on Arabic pages.
- **Schema changes:** the prod DB is **not** EF-migration-managed (it was restored from a .bak).
  - New tables/columns go in the **idempotent `IF NOT EXISTS` bootstrap in `Program.cs`**, next to EmailSettings, TenantDeviceTokens and BrandingProfiles.
  - Do not use ad-hoc prod sqlcmd scripts.
  - The CI `ef-migrations` job checks the model snapshot, so the design must say how the snapshot stays consistent.
  - A column **type** change needs `ALTER TABLE` run on prod inside the app_offline window. That is devops's job, and it needs the user's explicit yes.
- **Leftover `AspNetUsers` FKs are still latent.** Never put a username/email into these columns; use a real id or null:
  - `MaintenanceRequests.AssignedToUserId`
  - `Properties.ManagerId`
  - (`Documents.UploadedBy` already broke every upload once.)
- **Hangfire jobs:**
  - Jobs must be idempotent.
  - Never call `SaveChanges` per row in a loop: batch, fall back to per-row only on `DbUpdateException`, and call `ChangeTracker.Clear()` before notification loops.
  - The 5k-lease Scale tests catch O(N²) slowdowns.
- **Leases and units:**
  - The double-lease guard is `LeasesController.LiveLeaseConflictAsync`, covering Active+Pending with period overlap. Do not widen the DB unique index to Pending.
  - `Unit.Status` is a display flag. It is not the source of truth.
- **Payment rules (by design, not bugs):**
  - **Pay oldest unpaid invoice first.** The server returns 409 otherwise.
  - The amount is always `invoice.balance`, never `totalAmount`.
  - Cash from the app is a *request* that lands as PendingVerification in PaymentApproval.
  - The recorder cannot be the approver, except SuperAdmin.
  - `ReceiptNumber` is hidden from tenant APIs unless the status is Completed.
- **Rate limits:** `/api/tenant/login` and `/api/payments/knet/initiate` allow 10 requests per minute per IP. Repeated probes will get 429.
- **Web UI (Razor):**
  - Localization uses `IHtmlLocalizer<SharedResource>`; the key is the English text.
  - Arabic lives in `Resources/SharedResource.ar.resx`. A **duplicate `<data name>` breaks the build**, so append keys with a dedupe script. Never use sed: `&` corrupts the file.
  - `CaseInsensitiveHtmlLocalizer` already handles keys that differ only by case.
  - In view JS, use `@Json.Serialize(L["…"].Value)`. Never put `L["…"]` raw inside a quoted JS string.
  - Never nest `<a>` inside `<a>`.
  - Shared kits:
    - `wwwroot/css/list-pages.css` + `wwwroot/js/list-pages.js`: `.lp-*` list pages and `.pd-*` detail pages
    - `owner-dashboard.css`: `.od-*`
    - `forms.css` + `form-helpers.js`: the `.form-revamp` create forms
  - The tenant portal has **no layout**: `Views/TenantPortal/_ViewStart.cshtml` sets `Layout=null`, and each view carries its own `<head>`.
  - Tailwind is compiled with `npm run build:css` in `RentCollection.Web` (only if Tailwind classes change).
- **PDFs** use QuestPDF (`Infrastructure/Services/Pdf/QuestPdfService.cs`).
  - Keep LineHeight at 1.15 or above.
  - Hex colours are `#AARRGGBB`.
  - Append `\u200E` to Latin values in RTL documents.
  - One endpoint serves admin, portal and app: `?culture=ar|en`.

## KNET (KPay): verified facts. They override anything older, including `.claude/agents/kuwait-domain.md`.
- Encryption is in `KnetUtil`: AES-128-CBC, PKCS7, IV = Terminal Resource Key, uppercase hex.
  - `currencycode=414`.
  - `amt` is KWD formatted `0.000`. It is **not** fils.
- **Track ID is 20 characters or fewer**, or KNET truncates it and reports a duplicate track id.
- **responseURL and errorURL must be HTTPS** on `https://www.alrtam.co/api/payments/knet/response|error`. Over HTTP, KNET never captures.
- The callback arrives as `Content-Type: text/html`, so `[FromForm]` is empty.
  - **Read the raw body.** It is the bare uppercase hex `trandata` with no `trandata=` prefix.
  - A callback without valid encrypted trandata must be rejected.
- The response handler must reply with plain text **`REDIRECT=<absolute https url>`**. A 302 makes KNET reverse the capture.
- `Knet:Enabled` is a kill switch that only gates initiate. In-flight callbacks still complete.
- **KNET stays in sandbox** (`Knet:UseSandbox=true`, kpaytest.com.kw). That is the user's choice.
  - Never flip it to live.
  - Never add real KPay credentials anywhere.

## Stack — mobile (`RentCollectionTenantFlutter`)
- `lib/core` (constants, errors, theme, utils), `lib/data/{models,services}` (`api_service.dart` = Dio),
  `lib/presentation/{screens,viewmodels,widgets}`, `lib/l10n/app_{en,ar}.arb` (gen-l10n → run `flutter gen-l10n`
  after any .arb edit), `test/{core,financial}`, `integration_test/`.
- **Toolchain pin:** CI and the store workflows use **Flutter 3.24.5**, and `ci.yml` must stay pinned. Code must compile on 3.24.5:
  - Use no APIs newer than 3.24, e.g. use `withOpacity`, not `Color.withValues`.
  - Do not bump `intl` past `^0.19.0`.
- **Dependencies:** pub.dev may be unreachable from this machine (`package_info_plus` is not in the offline pub cache). Add no new package without design approval and a successful `flutter pub get`.
- `AppConstants.baseUrl` **defaults to PROD** (`https://www.alrtam.co/api`). For local work, always pass:
  - `--dart-define=API_BASE_URL=http://localhost:5032/api` (web/desktop), or
  - `http://10.0.2.2:5032/api` (Android emulator).
- **Dio:** never set a global `Content-Type` in `BaseOptions`. It silently breaks every multipart upload.
- **i18n:**
  - Every user-visible string goes in the .arb files. Status strings go through `localizeStatus(context, raw)`.
  - Dates go through `AppDateUtils`; money goes through `CurrencyUtils`. Both force **Western digits**. Never use a raw `DateFormat(...)`.
  - Use `TextAlign.end`, not `.right`, and direction-aware chevrons and arrows.
  - Watch interpolated strings like `'Invoice #$id'`.
- Payment flows follow the server rules above (oldest-first 409, `invoice.balance`).
  - `PaymentViewModel._tenantId` is legacy, so never gate the invoice-driven path on it.
  - `AppConstants.appVersion` is hard-coded. Bump it together with `pubspec.yaml` `version: x.y.z+N`. Android `versionCode` = N.
- **Baselines (keep or improve):**
  - `flutter analyze` = **0 issues**.
  - `flutter test` = all passing. It was 85 at v20; record the actual count you measure.
- **NEVER run two `flutter test` processes at once** on this machine.
  - Overlapping runs filled C: with test .dills in %TEMP%, and every test then failed to load.
  - Before starting, check that no `flutter_tester`/`dart` test run is active.
  - Run the whole suite once (about 6 s) rather than many parallel file runs.

## Local environment
- **Backend/web:** `cd C:\Users\r.shankar\RentCollectionSystem && dotnet run --project RentCollection.Web --launch-profile http`. It serves http://localhost:5032 in Development.
  - The connection string comes from **user-secrets** (Web project `UserSecretsId`). It points at `RAVIWST\SQLEXPRESS`/`RentCollectionDB`.
  - Never put it in appsettings.
- **The local DB has schema drift.** `dotnet ef database update` is blocked by `PendingModelChangesWarning`, and some tables were hand-created (see `RentCollection.Infrastructure/Scripts/LegalCases.sql`, `LookupItems.sql`). Don't "fix" drift by dropping or recreating anything without asking.
- A running app locks `bin/Debug`. To compile-check while it runs, use `dotnet build RentCollection.Web -o C:/Temp/rc-buildcheck`.
- **Tests:**
  - `dotnet test RentCollection.Tests/RentCollection.Tests.csproj`
  - `dotnet test RentCollection.IntegrationTests/RentCollection.IntegrationTests.csproj --filter "FullyQualifiedName!~SqlServerFixture&FullyQualifiedName!~AppVersionEndpointTests.GetVersion_ReturnsConfiguredMinSupportedVersion&FullyQualifiedName!~InvoicePdfEndpointTests"`
  - E2E needs the app running first: `dotnet test RentCollection.E2ETests` (env `RC_E2E_BASE_URL`, `RC_E2E_DB`).
  - Smoke: `pwsh scripts/smoke.ps1 [-BaseUrl …]`.
- **Local staff login:** use the probe pattern **on the local DB only**:
  1. Save a user's `PasswordHash`.
  2. Set a known v2 hash. PowerShell 5.1: `Rfc2898DeriveBytes(pw, salt, 600000, SHA256)`.
  3. Log in via the MVC form with `Email`, `Password` and `__RequestVerificationToken` plus a cookie jar.
  4. **Restore** the original hash.
- **Mobile:**
  - `cd C:\Users\r.shankar\RentCollectionTenantFlutter && flutter pub get && flutter analyze && flutter test`.
  - adb: `C:\Users\r.shankar\AppData\Local\Android\Sdk\platform-tools\adb.exe`.
  - The debug APK takes 20–30 s to start on the emulator.
- PowerShell: use the long temp path `C:\Users\r.shankar\AppData\Local\Temp`. The 8.3 form of `$env:TEMP` breaks `Remove-Item`.
- Git Bash's `python` is a Store stub, not real Python. Parse JSON with node or sed/grep.

## Live environment (facts only — credentials are NOT here)
- **Prod web/API:** `https://www.alrtam.co` (the SSL cert covers the apex too). The legacy http temp URL is `<DEMO_HOST>`.
  - Host: SmarterASP.NET shared IIS, FTP host `<FTP_HOST>`. The FTP site root holds the DLLs; there is no wwwroot prefix for them.
  - Static files go under FTP `/wwwroot/...`.
  - DB: `<DB_HOST>` / `<DB_NAME>`.
- **Always test live with `curl --compressed`.** Large uncompressed responses stall on this host and look like a phantom hang.
- **Demo identities:** demo admin `demo@alrtam.co`, demo tenant Civil ID `290010112345`. Their passwords are not stored in any agent file. Ask the user.
- **Live is customer-facing.** Never create test data there without the user's explicit yes and a cleanup plan.
  - `QaJourney` creates "QA Tower/QA Tester" records.
  - Read-only render checks and `LiveScreenshots` are fine.

## Where secrets live (never copy them into any file, prompt, log or notes)
| Secret | Lives in |
|---|---|
| Local DB connection, local Sentry DSN | `dotnet user-secrets` (RentCollection.Web) |
| Prod DB connection, KNET terminal creds, Sentry DSN | server-side appsettings on SmarterASP (edited via FTP), `appsettings.Production.json` git-ignored |
| Prod JWT signing key | SmarterASP App Pool env var |
| FTP password, SQL password (they DIFFER) | not stored — the user pastes them per session; remind them to rotate afterwards |
| Android keystore, Play service account, ASC API key | GitHub Actions secrets on `sphearsravi/alrtam-tenant` |
| Demo admin / demo tenant passwords | held by the user / app-review notes — ask |

## Hard rules (never break)
1. **Never commit or write secrets.** That means no passwords, keys, DSNs or connection strings with passwords in any tracked or `.claude/` file.
2. **Never push the working repos to a public remote.** `github.com/sphearsravi/rent` is a **public, scrubbed snapshot** at `C:\Users\r.shankar\rent`, not linked to these folders.
   - Never push working-dir code there.
   - Never re-sync to it without re-doing the scrub, which includes removing both `.claude/` dirs and `docs/sdlc` host details.
3. **Never overwrite on the server:**
   - `appsettings.json` / `appsettings.Production.json`
   - `web.config` (publish emits a stock one that drops env vars and stdout logging)
   - `App_Data/`: the `dpkeys` DataProtection key ring. Overwriting it logs everyone out and breaks antiforgery.
   - `wwwroot/uploads/`, `logs/`
4. Never remove the DataProtection persistence (`App_Data/dpkeys`, `SetApplicationName("RentCollection")`). Never remove the `Sentry:Dsn` key; empty is fine.
5. KNET stays sandbox. Never relax the trandata or HTTPS-callback rules.
6. The CI pin of Flutter 3.24.5 stays. Never run overlapping `flutter test`.
7. Deploys, prod DB DDL, pushes and every mobile release step need **explicit user confirmation in this conversation**, step by step.
8. Agents running in parallel own disjoint files.
9. The older agents (`reviewer`, `tester`, `release`, `kuwait-domain`) and the `/deploy`, `/smoke`, `/design` commands still exist. Where they conflict with this file, **this file wins**. Examples: KNET "fils/HMAC" and the http ktempurl smoke URL.
