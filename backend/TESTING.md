# Testing RentCollection

The suite is layered. Each layer catches a different class of regression at a
different cost. Run cheap layers often, expensive layers when you need them.

| Layer | Project | Tests | Runs in | Catches |
|---|---|---:|---|---|
| **Unit** | `RentCollection.Tests` | 150 | ~38 s | money math, MEC aggregator, RecurringJobs idempotency, calendar-march correctness, outbox failure injection, reflection drift, K-NET signing self-check, scale (5k leases), concurrency, recipient parsing |
| **Integration** | `RentCollection.IntegrationTests` | 13 | ~1 s | DI wiring, middleware order, `[Authorize]` gates, JSON shape contracts |
| **Browser E2E** | `RentCollection.E2ETests` | 23 | ~15 s | login page rendering, admin journeys, MEC page hydration, tenant portal journeys, /JobHealth visibility, invoice-PDF auth gate |
| **Operational** | `scripts/smoke.ps1`, `scripts/restore-drill.ps1` | n/a | ~3 s / ~60 s | post-deploy health, backup integrity + aggregator drift |

## Running the layers

```powershell
# Unit + integration (fast, every PR):
dotnet test RentCollection.Tests/RentCollection.Tests.csproj
dotnet test RentCollection.IntegrationTests/RentCollection.IntegrationTests.csproj `
  --filter "FullyQualifiedName!~SqlServerFixture&FullyQualifiedName!~AppVersionEndpointTests.GetVersion_ReturnsConfiguredMinSupportedVersion&FullyQualifiedName!~InvoicePdfEndpointTests"

# Browser E2E (boot backend first):
dotnet run --project RentCollection.Web      # in one terminal
dotnet test RentCollection.E2ETests/RentCollection.E2ETests.csproj   # in another

# Post-deploy smoke:
pwsh scripts/smoke.ps1                                  # localhost:5032
pwsh scripts/smoke.ps1 -BaseUrl https://rent.alrtam.com # against prod

# Quarterly restore drill:
pwsh scripts/restore-drill.ps1 -BackupPath "D:\backups\RentCollectionDB.bak"
```

## CI

GitHub Actions at [`.github/workflows/test.yml`](.github/workflows/test.yml).

- **unit + integration** job runs on every push and PR — ~3 min total.
- **e2e** job runs on push-to-`main` always, on PRs only when labelled `e2e`
  (avoids the Chromium boot cost on doc-only PRs). Tag a PR with
  `gh pr edit <num> --add-label e2e` to force the e2e job.

## Wiring `smoke.ps1` into your deploy pipeline

The script exits 0 / 1 / 2 / 3 — make the deploy step fail on any non-zero.

**GitHub Actions** (in a deploy workflow):
```yaml
- name: post-deploy smoke
  run: pwsh scripts/smoke.ps1 -BaseUrl ${{ secrets.DEPLOY_URL }}
```

**Azure DevOps** (release pipeline PowerShell task):
```yaml
- task: PowerShell@2
  displayName: 'smoke'
  inputs:
    targetType: filePath
    filePath: scripts/smoke.ps1
    arguments: '-BaseUrl $(DeployUrl)'
    pwsh: true
    errorActionPreference: stop
```

**IIS / on-prem** (post-deploy hook):
```powershell
pwsh "$env:Deploy.Root\scripts\smoke.ps1" -BaseUrl "http://172.16.1.66:99/"
if ($LASTEXITCODE -ne 0) { throw "smoke failed — rolling back" }
```

## When to add what

| If you're … | Add a test in … |
|---|---|
| Touching `LeaseMath`, `RecurringJobs`, `MonthEndCloseService`, or any other domain logic | `RentCollection.Tests` |
| Adding a new controller / endpoint / `[Authorize]` boundary | `RentCollection.IntegrationTests` |
| Adding a new page or changing a critical user journey | `RentCollection.E2ETests` |
| Adding a new health-relevant subsystem (cache, queue, external API) | a check in `Program.cs` + a probe in `smoke.ps1` |
| Adding a new aggregator that goes into a snapshot/audit table | extend `RentCollection.OpsCli` so the restore drill can verify it |

## Test-environment quirks

- **Backend must be live for E2E** — start with `dotnet run --project RentCollection.Web` first.
- **EF in-memory `Any()` doesn't see un-saved Adds.** Seeding helpers must check
  `_ctx.Set<T>().Local` first or call `SaveChanges` between adds. See the
  comment in `RentCollectionTests.Jobs.RecurringJobsTests.SeedLease`.
- **JSON casing**: MVC sends camelCase; `JsonSerializer.Serialize(...)` defaults
  to source-case. When asserting service output directly use
  `JsonNamingPolicy.CamelCase` so the test matches what JS reads.
- **Hangfire smoke**: `/health/ready` includes a `hangfire` check that fails if
  no server has heartbeated in 60s. False positives can happen during cold start
  (~5–10 s window) — give the deploy step a short warmup before the smoke step.
- **Demo credentials**: the seeded admin is `admin@test.com / Admin@123`
  (matches the `Fill demo credentials` button on the login page). The E2E
  suite uses these — if you change them, update [`AdminJourneyTests`](RentCollection.E2ETests/AdminJourneyTests.cs).

## Sprint 6 additions (operational gaps)

| Gap | Now covered by |
|---|---|
| Authenticated tenant journey | [`TenantJourneyTests`](RentCollection.E2ETests/TenantJourneyTests.cs) — login as seeded test tenant via [`TenantSeedFixture`](RentCollection.E2ETests/Helpers/TenantSeedFixture.cs), navigate dashboard + payment history, verify the portal links to the invoice-PDF API |
| Concurrency / scale | [`ScaleAndConcurrencyTests`](RentCollection.Tests/Scale/ScaleAndConcurrencyTests.cs) — 5000-lease rent generation, 3× idempotency, parallel-run safety, 50-cycle memory budget |
| K-NET contract | [`KnetContractTests`](RentCollection.Tests/Knet/KnetContractTests.cs) — encrypt/decrypt round-trip, hex format invariants, 3-decimal KWD precision, env-gated sandbox connectivity probe |
| Hangfire failure visibility | [`/JobHealth`](RentCollection.Web/Controllers/JobHealthController.cs) admin view + [`HangfireFailureAlertFilterTests`](RentCollection.Tests/Jobs/HangfireFailureAlertFilterTests.cs) for recipient-parsing contract |

### Seeded test tenant (used by `TenantJourneyTests`)

`TenantSeedFixture` upserts this row on every E2E run:

| Field | Value |
|---|---|
| Civil ID (login) | `999000000001` |
| Password | `E2eTest@123` |
| Email | `e2e-test-tenant@example.com` |
| Property / Unit | `E2E Test Tower / E2E-1` |

It's a real row in the live dev DB — you can sign in by hand for debugging.
For CI against a Dockerised SQL instance, set `RC_E2E_DB` env var.

### Scale tests (`[Trait("Category", "Scale")]`)

```powershell
# Run only the scale suite:
dotnet test RentCollection.Tests --filter "Category=Scale"

# Skip them on regular runs (CI does this for the PR job):
dotnet test RentCollection.Tests --filter "Category!=Scale"
```

### K-NET sandbox probe

The signing self-check runs unconditionally; the live sandbox probe needs:

```powershell
$env:KNET_SANDBOX_TRANPORTAL_ID = "<id>"
$env:KNET_SANDBOX_TRANPORTAL_PWD = "<pwd>"
$env:KNET_SANDBOX_KEY            = "<16-char key>"
dotnet test RentCollection.Tests --filter "Category=Contract"
```

Without those env vars the probe logs `[skip]` and counts as passed —
"no creds to verify" is the right semantic, not a failure.

## What's still NOT covered

- **Real-time SignalR / WebSocket flows** — none in the system today, but if
  added (e.g. live payment notification), build a Playwright test that
  asserts the JS receiver fires.
- **Long-horizon soak** — 7 days idle, real load profile. Current scale
  tests prove correctness at volume, not stability over time.
- **SQL Server-specific behaviour** — the EF InMemory provider used by unit
  tests can't catch unique-index violations, transaction isolation quirks,
  or trigger interactions. Tests at this level live in
  `RentCollection.IntegrationTests.SqlServerFixture` (Docker-gated) and run
  in a nightly CI job.
