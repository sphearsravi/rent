/// Platform-aware URL launcher
/// Uses conditional imports to pick web (dart:html) or mobile (url_launcher)
export 'url_launcher_stub.dart'
    if (dart.library.html) 'url_launcher_web.dart';
