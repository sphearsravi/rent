import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:viijar_tenant/core/errors/failure.dart';
import 'package:viijar_tenant/data/services/api_service.dart';
import 'package:viijar_tenant/l10n/app_localizations.dart';

Future<String> _renderFailureMessage(
  WidgetTester tester,
  Failure failure,
  Locale locale,
) async {
  late BuildContext ctx;
  await tester.pumpWidget(
    MaterialApp(
      locale: locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Builder(
        builder: (c) {
          ctx = c;
          return const SizedBox.shrink();
        },
      ),
    ),
  );
  return failure.localizedMessage(ctx);
}

void main() {
  group('Failure.fromException', () {
    test('maps timeout to TimeoutFailure', () {
      final f = Failure.fromException(
        ApiException('x', kind: ApiErrorKind.timeout),
      );
      expect(f, isA<TimeoutFailure>());
    });

    test('maps 401 to UnauthenticatedFailure and requires reauth', () {
      final f = Failure.fromException(
        ApiException('x', kind: ApiErrorKind.unauthenticated),
      );
      expect(f, isA<UnauthenticatedFailure>());
      expect(f.requiresReauth, isTrue);
    });

    test('non-ApiException maps to UnknownFailure preserving the original', () {
      final original = StateError('boom');
      final f = Failure.fromException(original);
      expect(f, isA<UnknownFailure>());
      expect((f as UnknownFailure).error, same(original));
    });
  });

  group('Failure.localizedMessage', () {
    testWidgets('TimeoutFailure resolves to English', (tester) async {
      final msg = await _renderFailureMessage(
        tester,
        const TimeoutFailure(),
        const Locale('en'),
      );
      expect(msg, 'The request timed out. Please try again.');
    });

    testWidgets('TimeoutFailure resolves to Arabic', (tester) async {
      final msg = await _renderFailureMessage(
        tester,
        const TimeoutFailure(),
        const Locale('ar'),
      );
      expect(msg, 'انتهت مهلة الطلب. حاول مرة أخرى.');
    });

    testWidgets('BadRequestFailure passes the backend message through unchanged',
        (tester) async {
      final msg = await _renderFailureMessage(
        tester,
        const BadRequestFailure('Invoice already paid'),
        const Locale('en'),
      );
      expect(msg, 'Invoice already paid');
    });
  });
}
