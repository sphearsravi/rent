import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'package:viijar_tenant/main.dart' as app;
import 'package:viijar_tenant/presentation/screens/dashboard_screen.dart';
import 'package:viijar_tenant/presentation/screens/invoices_screen.dart';
import 'package:viijar_tenant/presentation/widgets/legal_case_banner.dart';

/// End-to-end UI verification against the LIVE backend (www.alrtam.co).
/// Logs in as tenant 711 (temp password set by the harness), then proves the
/// legal-case banner (dashboard) and the rental/invoices screen actually paint
/// with real data. Screenshots are saved via test_driver/integration_test.dart.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  // Pump repeatedly until [f] appears or [seconds] elapse. pumpAndSettle can't
  // be used across network waits (frames keep getting scheduled), so we poll.
  Future<bool> waitFor(WidgetTester tester, Finder f, {int seconds = 40}) async {
    final end = DateTime.now().add(Duration(seconds: seconds));
    while (DateTime.now().isBefore(end)) {
      await tester.pump(const Duration(milliseconds: 400));
      if (f.evaluate().isNotEmpty) return true;
    }
    return false;
  }

  Future<void> shot(WidgetTester tester, String name) async {
    await binding.convertFlutterSurfaceToImage();
    await tester.pump(const Duration(milliseconds: 300));
    await binding.takeScreenshot(name);
  }

  testWidgets('legal-case banner + rental screens render for tenant 711',
      (tester) async {
    app.main();
    await tester.pump(const Duration(seconds: 4));

    // --- Login screen ---
    final fields = find.byType(TextFormField);
    expect(await waitFor(tester, fields), isTrue,
        reason: 'login fields never appeared',);
    await tester.enterText(fields.at(0), '201011083576'); // Civil ID
    await tester.enterText(fields.at(1), 'Verify#711!'); // temp password
    await tester.pump(const Duration(milliseconds: 400));
    await shot(tester, '01-login-filled');

    await tester.tap(find.byType(ElevatedButton).first);

    // --- Dashboard (legal-case banner) ---
    final gotDash = await waitFor(tester, find.byType(DashboardScreen));
    expect(gotDash, isTrue, reason: 'dashboard did not load after login');
    // Let the dashboard fetch resolve and the banner build.
    await waitFor(tester, find.byType(LegalCaseBanner), seconds: 25);
    await tester.pump(const Duration(seconds: 1));
    await shot(tester, '02-dashboard-legalcase');
    expect(find.byType(LegalCaseBanner).evaluate().isNotEmpty, isTrue,
        reason: 'LegalCaseBanner not present on dashboard for tenant 711',);

    // --- Invoices (rental) ---
    await tester.tap(find.byIcon(Icons.receipt_long_rounded));
    expect(await waitFor(tester, find.byType(InvoicesScreen), seconds: 20),
        isTrue,
        reason: 'invoices screen did not open',);
    await tester.pump(const Duration(seconds: 2));
    await shot(tester, '03-invoices-rental');
  });
}
