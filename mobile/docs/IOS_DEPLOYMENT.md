# iOS Deployment Plan — VIIJAR Tenant (App Store)

> There was no iOS plan before this; the only iOS artefact was `codemagic.yaml`.
> `docs/RELEASE.md` is Android/web only and is stale (wrong API URL + keystore
> alias). This file is the source of truth for shipping the iPhone app.

## The core fact: no Mac required
iOS apps can only be *built* on macOS. This repo builds iOS in the cloud on
**Codemagic** (a hosted Mac) → produces a signed `.ipa` → uploads to
**TestFlight** → you promote to the App Store. Pipeline: `codemagic.yaml`,
workflow **`ios-release`**.

## Current readiness
| Item | State |
|---|---|
| Bundle ID `com.viijar.tenant`, version 1.0 | ✅ |
| Info.plist: display name, camera/photo usage, `ITSAppUsesNonExemptEncryption=false` | ✅ |
| App icon 1024 (no alpha) | ✅ |
| `codemagic.yaml` API → `https://www.alrtam.co/api` | ✅ |
| iOS screenshots 1290×2796 (`play_store_assets/ios_screenshot_*`) | ✅ |
| Privacy policy (public) `https://alrtam.co/privacy.html` | ✅ |
| `APP_STORE_APPLE_ID` in codemagic.yaml | ⚠️ placeholder `0000000000` |
| Device family = universal (iPhone + iPad) | ⚠️ forces iPad screenshots unless set iPhone-only |
| iOS push (GoogleService-Info.plist + APNs key) | ❌ not set up — defer post-launch |
| Apple Developer Program membership ($99/yr) | ❓ must be active on Moh.rush98@gmail.com |

## Phased plan

### Phase 0 — Prerequisite (YOU)
- Apple Developer Program membership **$99/year**, active on `Moh.rush98@gmail.com`.
  Nothing below works until this is paid & approved.

### Phase 1 — App Store Connect: create the app (YOU, web)
1. `appstoreconnect.apple.com` → **My Apps → + → New App**.
2. Platform **iOS**; Name **VIIJAR Tenant**; Primary language **English**;
   Bundle ID **com.viijar.tenant** (register it under Certificates,
   Identifiers & Profiles if it isn't listed); SKU e.g. `viijar-tenant-001`.
3. Open the app → copy its **Apple ID** (a number) → **send it to me**.

### Phase 2 — Connect Codemagic to Apple (YOU, web)
1. App Store Connect → **Users and Access → Integrations → App Store Connect API**
   → generate an **API key** (role App Manager). Download the `.p8`; note **Key ID**
   and **Issuer ID**.
2. Codemagic → **Teams → Integrations → Developer Portal** → add that key,
   name the integration exactly **`ViijarASCKey`**.
3. Codemagic → **Add application** → connect the git repo → it reads
   `codemagic.yaml`.

### Phase 3 — Config (ME, once you send the Apple ID number)
- Set `APP_STORE_APPLE_ID` in `codemagic.yaml`.
- (Optional, recommended) set the app **iPhone-only** to avoid iPad screenshots.

### Phase 4 — Build & upload (Codemagic, automatic)
- Run the **`ios-release`** workflow. Codemagic builds on a Mac, auto-signs via
  the API key, uploads to **TestFlight**, emails the IPA to `sphearsravi@gmail.com`.
  ~15–30 min.

### Phase 5 — TestFlight (YOU)
- Install **TestFlight** on an iPhone → install the build → verify login,
  invoices, KNET payment, maintenance photo upload.

### Phase 6 — Store listing + submit (YOU, web)
- App Store Connect → your app → add: **screenshots** (`ios_screenshot_*`),
  **description** (reuse Android), **keywords**, **support URL**,
  **privacy URL** `https://alrtam.co/privacy.html`, **category**, **age rating**.
- **Submit for Review.** Apple review is typically **~24–48h**.

### Phase 7 — Release
- Once **Approved**, release to the App Store (manual or automatic).

## Blockers, in order
1. **Membership active?** (Phase 0) — the gate for everything.
2. **iPhone-only vs iPad** — decide before the store listing (iPad needs extra screenshots).
3. **Push notifications** — needs a real Firebase iOS app + an APNs auth key in
   App Store Connect. Not required for first launch; wire up later.
