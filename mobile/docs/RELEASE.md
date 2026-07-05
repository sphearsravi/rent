# Release runbook

This project does not yet ship multiple Android product flavors / iOS schemes.
Until that is done, environment selection is via `--dart-define` and the
backend URL **must** be supplied at build time for any non-dev binary.

## Build commands

### Development (default — talks to http://localhost:5032/api)
```
flutter run -d chrome
flutter run -d <android-device>
```

### Staging
```
flutter build apk --release \
  --dart-define=API_BASE_URL=https://staging-api.viijar.com/api

flutter build web --release \
  --dart-define=API_BASE_URL=https://staging-api.viijar.com/api
```

### Production
```
flutter build apk --release \
  --dart-define=API_BASE_URL=https://api.viijar.com/api

flutter build appbundle --release \
  --dart-define=API_BASE_URL=https://api.viijar.com/api

flutter build web --release \
  --dart-define=API_BASE_URL=https://api.viijar.com/api
```

## Android signing

`android/key.properties` must exist for `flutter build apk --release` to
succeed — the Gradle build now throws `GradleException` rather than silently
fall back to debug signing.

```
storePassword=...
keyPassword=...
keyAlias=upload
storeFile=path/to/upload-keystore.jks
```

Keep the keystore + this file **out of git**.

## KNET callback deeplink

On Android, the `viijar://payment/result?paymentId=...&status=...` intent
filter (see `android/app/src/main/AndroidManifest.xml`) returns the user to
the app after KNET completes. The backend's `/api/payments/knet/response`
endpoint must redirect to this scheme. Plugin `app_links` should be wired in
`main.dart` to parse the incoming URI and push `/payments/result`.

## Force update

`lib/core/version_gate.dart` contains the comparison logic. The splash screen
should call a backend endpoint (TODO: `/api/app/version?platform=android`)
that returns:

```json
{ "minSupportedVersion": "1.2.0", "latestVersion": "1.3.0" }
```

and route to a blocking "Update required" screen when `VersionGate.evaluate`
returns `UpdateStatus.required`.

## CI checklist before release

- [ ] `flutter analyze` clean
- [ ] `flutter test` green
- [ ] `--dart-define=API_BASE_URL` is **not** localhost
- [ ] `key.properties` available on the build agent (Android)
- [ ] App version in `pubspec.yaml` bumped
- [ ] Web `manifest.json` and `index.html` reflect current brand
