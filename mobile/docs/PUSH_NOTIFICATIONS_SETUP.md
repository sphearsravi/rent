# Push Notifications Setup (Sprint 26 Phase 2)

This document walks through enabling Firebase Cloud Messaging (FCM) push
notifications for the ALRTAM tenant Flutter app + the RentCollection
server. Once these steps are complete, tenants will receive native push
notifications for: rent due, payment received, maintenance update, lease
expiring.

**Estimated time:** 15-30 minutes (most is one-time Firebase setup).

**Prerequisites:**
- A Google account
- Access to the GitHub/local copy of `RentCollectionTenantFlutter`
- Access to the RentCollection server (SmarterASP FTP)

---

## Part 1 — Create the Firebase project (Google side)

### 1.1 Create the project
1. Go to https://console.firebase.google.com
2. Click **Add project**
3. Project name: `RentCollection Tenant` (or whatever you prefer)
4. Disable Google Analytics (not needed for push)
5. Click **Create project** → wait ~30 seconds

### 1.2 Add Android app
1. In Firebase Console → click the Android icon
2. **Package name:** `com.viijar.tenant` (must match `android/app/build.gradle` `applicationId`)
3. **App nickname:** "ALRTAM Tenant Android"
4. **Debug signing certificate SHA-1:** leave blank for now (only needed for Google Sign-In)
5. Click **Register app**
6. **Download `google-services.json`** — this is the critical file
7. Place it at `RentCollectionTenantFlutter/android/app/google-services.json`
   - That folder, not `android/`
   - Do NOT commit this file — add to `.gitignore`
8. Skip the "Add Firebase SDK" instructions on screen — the Flutter `firebase_core` package handles that
9. Click **Continue to console**

### 1.3 (Optional) Add iOS app
Skip if you're not building iOS. Same flow:
1. **Bundle ID:** check your `ios/Runner.xcodeproj` for the bundle identifier
2. Download `GoogleService-Info.plist`
3. Drop into `ios/Runner/`

### 1.4 Get the FCM Server Key (for the .NET server)
1. Firebase Console → ⚙️ icon (Settings) → **Project settings**
2. Tab: **Cloud Messaging**
3. If you see a banner about "Cloud Messaging API (Legacy) disabled" → click **Enable** (this is what the .NET server uses for the simplest send path)
4. Once enabled, you'll see **Server key** — copy it. Save it somewhere safe — it's a secret.

**OR** use the newer HTTP v1 API (more secure, recommended for production):
1. Same page, scroll to **Service accounts** tab in left menu
2. Click **Generate new private key** → downloads a JSON file
3. Save as `firebase-admin-sdk.json` somewhere ALRTAM-only (don't commit)
4. The server will read this file at startup

Pick one. Server-key is simpler; service-account is the modern path.

---

## Part 2 — Configure the .NET server

### 2.1 Add the FCM Server Key to SmarterASP

Open `appsettings.json` on the SmarterASP server (via FTP File Manager):

```json
{
  "Fcm": {
    "ServerKey": "PASTE_YOUR_FCM_SERVER_KEY_HERE",
    "Enabled": true
  }
}
```

Save the file. The app will pick it up on next worker recycle (touch
`web.config` to force one).

### 2.2 Schema bootstrap

A new table `TenantDeviceTokens` will be auto-created on first startup
(same idempotent SQL pattern as `EmailSettings` in Sprint 25).

### 2.3 Verify the server is dispatching

After Step 1-4 above are done, log into the admin portal at
`/EmailSettings`-style page (a new `/PushSettings` page will be added in
the next sprint), or check the Hangfire dashboard at `/hangfire` for the
`process-fcm-outbox` recurring job firing every minute.

---

## Part 3 — Build the Flutter app

Once `android/app/google-services.json` is in place:

```bash
cd C:\Users\r.shankar\RentCollectionTenantFlutter

# Restore pub packages (firebase_core + firebase_messaging are pre-added)
flutter pub get

# Build the APK pointing at the demo backend
flutter build apk --debug --dart-define=API_BASE_URL=http://<DEMO_HOST>/api
```

The output APK is at `build/app/outputs/flutter-apk/app-debug.apk`.

### First run flow
1. Install the APK on the test phone
2. Open the app → log in as tenant
3. App will request notification permission (Android 13+) — tap **Allow**
4. Background: the app silently registers its FCM token with the server
   via `POST /api/tenant/{tenantId}/register-device`
5. Now any notification the server emits (rent due, payment received,
   etc.) will arrive as a native push on the phone — even when the app
   is closed

### Test sending a push
From the server-side admin portal (once `/PushSettings` is added), there
will be a "Send Test Push" button — analogous to the Email Settings test.

For now, you can test directly from Firebase Console:
1. Firebase Console → **Cloud Messaging** → **Send your first message**
2. Notification title: "Test"
3. Notification text: "Hello from Firebase"
4. Target: your specific FCM token (copy it from the app's debug log on first launch — search for `[FCM] Token:`)
5. Send

---

## Troubleshooting

### "google-services.json is missing" at build time
You forgot Part 1.2 step 7. Drop the file at
`RentCollectionTenantFlutter/android/app/google-services.json` and re-run
`flutter pub get` + the build command.

### Permission denied on Android 13+
Android 13+ requires runtime permission for notifications. The app
requests it on first launch; if the user denies, no pushes will arrive.
They can re-grant via Settings → Apps → ALRTAM Tenant → Notifications.

### iOS pushes silent
iOS requires:
- An Apple Developer Program membership ($99/year)
- An APNS auth key uploaded to Firebase Console → Project Settings → Cloud Messaging → Apple app configuration

This is more involved than Android — skip iOS until you have an Apple
Developer account.

### Server logs say "FCM dispatch skipped — Fcm:Enabled=false"
Step 2.1 wasn't done, or `"Enabled": true` is missing. Re-check the
JSON section.

### Token registers but pushes never arrive
1. Confirm `Fcm:ServerKey` matches Firebase Console exactly (no leading/trailing whitespace)
2. Check the device's FCM token in the `TenantDeviceTokens` table
3. Check the Notifications table for `Channel="Push"` rows — are they
   `Pending` (dispatcher not running) or `Failed` (FCM rejected; see
   `LastError` column for reason)?

---

## Costs

- **Firebase Cloud Messaging:** free for unlimited messages
- **Apple Developer Program** (iOS only): $99/year
- No per-message fee from Apple/Google for push notifications

---

## When you've finished this setup

Tell me, and I'll flip on the rest of the wiring:
- Server-side FCM dispatcher (currently stubbed)
- Mobile FCM service initialization at app startup
- Admin `/PushSettings` page with test button
- New Notifications-table channel value for `Push`

That last bit is a 2-hour sprint once your Firebase project is alive.
