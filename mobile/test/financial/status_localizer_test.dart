import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:viijar_tenant/core/utils/status_localizer.dart';
import 'package:viijar_tenant/l10n/app_localizations.dart';

void main() {
  Future<BuildContext> pumpAndCapture(WidgetTester tester) async {
    late BuildContext captured;
    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        locale: const Locale('en'),
        home: Builder(
          builder: (context) {
            captured = context;
            return const SizedBox.shrink();
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
    return captured;
  }

  testWidgets('known statuses localize to English labels', (tester) async {
    final ctx = await pumpAndCapture(tester);

    expect(localizeStatus(ctx, 'Pending'), 'Pending');
    expect(localizeStatus(ctx, 'Paid'), 'Paid');
    expect(localizeStatus(ctx, 'Overdue'), 'Overdue');
    expect(localizeStatus(ctx, 'Failed'), 'Failed');
  });

  testWidgets('matching is case- and separator-insensitive', (tester) async {
    final ctx = await pumpAndCapture(tester);

    expect(localizeStatus(ctx, 'in_progress'), 'In Progress');
    expect(localizeStatus(ctx, 'In Progress'), 'In Progress');
  });

  testWidgets('unknown status falls back to original string', (tester) async {
    final ctx = await pumpAndCapture(tester);

    expect(localizeStatus(ctx, 'ZzUnknown'), 'ZzUnknown');
  });
}
