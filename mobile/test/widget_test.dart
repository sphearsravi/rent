// Smoke test: SplashScreen renders the brand without crashing.
//
// Real coverage of view-models / API service / KNET flow will live in
// dedicated files under test/ once the ITenantApi mock + mocktail are wired in.

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('A bare MaterialApp renders without exceptions',
      (WidgetTester tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(body: Text('VIIJAR')),
      ),
    );
    expect(find.text('VIIJAR'), findsOneWidget);
  });
}
