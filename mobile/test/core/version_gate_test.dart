import 'package:flutter_test/flutter_test.dart';
import 'package:viijar_tenant/core/version_gate.dart';

void main() {
  group('AppVersion.parse', () {
    test('strips build number and pre-release suffix', () {
      expect(AppVersion.parse('1.2.3+45').toString(), '1.2.3');
      expect(AppVersion.parse('1.2.3-beta').toString(), '1.2.3');
    });

    test('defaults missing components to zero', () {
      expect(AppVersion.parse('2').toString(), '2.0.0');
      expect(AppVersion.parse('2.1').toString(), '2.1.0');
    });
  });

  group('VersionGate.evaluate', () {
    test('blocks when running version is below minimum', () {
      expect(
        VersionGate.evaluate(current: '1.0.0', minSupported: '1.1.0'),
        UpdateStatus.required,
      );
    });

    test('nudges when latest is newer than running', () {
      expect(
        VersionGate.evaluate(
          current: '1.1.0',
          minSupported: '1.0.0',
          latest: '1.2.0',
        ),
        UpdateStatus.optional,
      );
    });

    test('ok when running >= latest', () {
      expect(
        VersionGate.evaluate(
          current: '2.0.0',
          minSupported: '1.0.0',
          latest: '1.9.9',
        ),
        UpdateStatus.ok,
      );
    });
  });
}
