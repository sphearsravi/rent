/// Web implementation - uses dart:html to redirect browser
library;
// ignore: avoid_web_libraries_in_flutter
import 'dart:html' as html;

Future<void> launchExternalUrl(String url) async {
  html.window.open(url, '_blank');
}

void redirectToUrl(String url) {
  html.window.location.href = url;
}
