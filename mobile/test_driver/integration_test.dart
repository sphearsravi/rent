import 'dart:io';

import 'package:integration_test/integration_test_driver_extended.dart';

/// Driver for the end-to-end UI test. Saves each screenshot the test requests
/// to build/screenshots/<name>.png on the host machine.
Future<void> main() async {
  await integrationDriver(
    onScreenshot:
        (String name, List<int> bytes, [Map<String, Object?>? args]) async {
      final file = File('build/screenshots/$name.png');
      if (!await file.parent.exists()) {
        await file.parent.create(recursive: true);
      }
      await file.writeAsBytes(bytes);
      return true;
    },
  );
}
