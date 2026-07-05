# RentCollection

Kuwait rent-collection system — a .NET 8 web/admin portal + API and a Flutter tenant mobile app. Tenants view invoices and pay via **KNET**, **Cash**, or **Bank Transfer**; admins generate invoices, approve payments (two-person control), issue refunds, and run financial reports.

## Repository layout

```
rent/
├── backend/   # .NET 8 — MVC admin portal + REST API + Hangfire jobs (RentCollection.*)
└── mobile/    # Flutter — VIIJAR tenant app (com.viijar.tenant)
```

## Backend (`backend/`)

.NET 8, EF Core (SQL Server), Hangfire, Serilog, cookie auth.

```bash
cd backend
dotnet build RentCollection.sln
dotnet run --project RentCollection.Web        # dev at http://localhost:5032
dotnet test                                    # unit + integration + E2E test projects
```

**Configuration / secrets** — real secrets are **not** in the repo. `appsettings.json` is a template with empty values. Provide real values via `appsettings.Production.json` (gitignored) or environment variables:
- `ConnectionStrings:DefaultConnection` — SQL Server connection string
- `Knet:TranportalId` / `TranportalPwd` / `TerminalResourceKey` — KNET (KPay) merchant credentials
- `Jwt:Key` — 32+ char signing key
- `Sentry:Dsn` — set to `""` to disable

## Mobile (`mobile/`)

Flutter tenant app. The API base URL **must** be supplied at build time:

```bash
cd mobile
flutter pub get
flutter run   --dart-define=API_BASE_URL=https://your-api-host/api
flutter build apk --release --dart-define=API_BASE_URL=https://your-api-host/api
flutter test test/financial/                   # money-logic unit/viewmodel tests
```

- Localized **English + Arabic (RTL)**; run `flutter gen-l10n` after editing `lib/l10n/*.arb`.
- Push notifications need a real `android/app/google-services.json` (a placeholder is committed) — see `mobile/docs/PUSH_NOTIFICATIONS_SETUP.md`.
- Release signing needs `android/key.properties` + a keystore (both gitignored); without them the release build uses the debug key.

## KNET test card (test gateway)

| Field | Value |
|---|---|
| Card | `8888 8800 0000 0001` |
| Expiry (success) | `09/30` → Captured |
| Expiry (any other) | Declined |
| PIN | `1234` |
