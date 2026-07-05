/// Force-update gate. Backend exposes:
///   GET /api/app/version?platform=android|ios
///   -> { "minSupportedVersion": "1.2.0", "latestVersion": "1.3.0" }
///
/// On splash, the app compares the running version against
/// `minSupportedVersion`. If older, render a non-dismissible blocking screen
/// pointing at the store URL. `latestVersion` is informational — surface a
/// soft "update available" banner if it's greater than the running version.
///
/// This file intentionally contains no networking: implement the actual fetch
/// in your API service once the endpoint exists. Comparison logic stays here
/// so it can be unit-tested.

class AppVersion {
  AppVersion(this.major, this.minor, this.patch);

  final int major;
  final int minor;
  final int patch;

  /// Parses semver-ish strings like "1.2.3" or "1.2.3+45". Suffix after `+`
  /// is ignored (it's the build number on Flutter, not part of the comparison).
  factory AppVersion.parse(String input) {
    final cleaned = input.split('+').first.split('-').first;
    final parts = cleaned.split('.');
    int safe(int i) => i < parts.length ? int.tryParse(parts[i]) ?? 0 : 0;
    return AppVersion(safe(0), safe(1), safe(2));
  }

  /// Negative if `this` is older, zero if equal, positive if newer.
  int compareTo(AppVersion other) {
    if (major != other.major) return major - other.major;
    if (minor != other.minor) return minor - other.minor;
    return patch - other.patch;
  }

  bool isOlderThan(AppVersion other) => compareTo(other) < 0;

  @override
  String toString() => '$major.$minor.$patch';
}

enum UpdateStatus {
  /// Running version is at or above `minSupportedVersion` — let the user in.
  ok,

  /// `latestVersion` is newer — show a non-blocking nudge.
  optional,

  /// Running version is below `minSupportedVersion` — block the UI.
  required,
}

class VersionGate {
  /// Pure: caller fetches the server's `minSupportedVersion`/`latestVersion`
  /// strings and the current app version (via `package_info_plus`), then asks
  /// this helper what state we're in.
  static UpdateStatus evaluate({
    required String current,
    required String minSupported,
    String? latest,
  }) {
    final c = AppVersion.parse(current);
    final m = AppVersion.parse(minSupported);
    if (c.isOlderThan(m)) return UpdateStatus.required;
    if (latest != null) {
      final l = AppVersion.parse(latest);
      if (c.isOlderThan(l)) return UpdateStatus.optional;
    }
    return UpdateStatus.ok;
  }
}
