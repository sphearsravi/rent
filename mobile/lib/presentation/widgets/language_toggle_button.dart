import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/locale_provider.dart';

/// One-tap top-bar button to switch the app language between English and
/// Arabic. Shows a globe + the language you'll switch TO ("ع" while in
/// English, "EN" while in Arabic), so it's obvious what the tap does.
///
/// Pass [color] to match the surface it sits on (defaults to white for
/// coloured app bars; use a dark colour on light backgrounds like login).
class LanguageToggleButton extends StatelessWidget {
  const LanguageToggleButton({super.key, this.color});

  final Color? color;

  @override
  Widget build(BuildContext context) {
    final isArabic = context.watch<LocaleProvider>().isArabic;
    final c = color ?? Colors.white;
    final nextLabel = isArabic ? 'EN' : 'ع';
    return TextButton.icon(
      onPressed: () => context.read<LocaleProvider>().toggle(),
      icon: Icon(Icons.language, size: 20, color: c),
      label: Text(
        nextLabel,
        style: TextStyle(color: c, fontWeight: FontWeight.w700, fontSize: 15),
      ),
      style: TextButton.styleFrom(
        foregroundColor: c,
        padding: const EdgeInsets.symmetric(horizontal: 8),
        minimumSize: const Size(44, 44),
      ),
    );
  }
}
