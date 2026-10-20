import 'dart:io' show File;

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';
import 'package:url_launcher/url_launcher.dart';

import '../constants/app_constants.dart';
import '../../data/services/storage_service.dart';
import '../../l10n/app_localizations.dart';

/// Downloads `/api/tenant/{tenantId}/invoices/{invoiceId}/invoice.pdf` with
/// the tenant JWT attached and opens it in the platform's PDF viewer.
///
/// - **Mobile**: writes bytes to a temp file and hands it to the system viewer.
/// - **Web**: opens the URL in a new tab. The browser request will not carry
///   the Bearer token from secure storage, so the server returns 302 to the
///   admin login (cookie scheme's challenge) — to be fixed in a follow-up by
///   either accepting a short-lived `?token=` query param or doing the
///   fetch + Blob trick in JS.
///
/// Mirrors [ReceiptLauncher] — same flow, different endpoint. Worth merging
/// into a shared `PdfDownloader` helper once a third caller appears.
class InvoiceLauncher {
  static Future<void> open(
    BuildContext context, {
    required int tenantId,
    required int invoiceId,
  }) async {
    // Resolve everything that needs `context` BEFORE the first await — using
    // the BuildContext across async gaps can hit a disposed widget.
    final messenger = ScaffoldMessenger.of(context);
    final l = AppLocalizations.of(context)!;
    // The server renders the PDF in the requested language (Arabic = RTL
    // layout + Arabic labels); without this it falls back to English.
    final culture =
        Localizations.localeOf(context).languageCode.startsWith('ar') ? 'ar' : 'en';
    final path = '/tenant/$tenantId/invoices/$invoiceId/invoice.pdf?culture=$culture';

    try {
      if (kIsWeb) {
        final uri = Uri.parse('${AppConstants.baseUrl}$path');
        await launchUrl(uri, webOnlyWindowName: '_blank');
        return;
      }

      final storage = StorageService();
      await storage.init();
      final token = await storage.getToken();
      if (token == null || token.isEmpty) {
        messenger.showSnackBar(SnackBar(content: Text(l.sessionRelogin)));
        return;
      }

      final dio = Dio(BaseOptions(
        baseUrl: AppConstants.baseUrl,
        responseType: ResponseType.bytes,
        headers: {'Authorization': 'Bearer $token'},
      ),);

      final response = await dio.get(path);
      if (response.statusCode != 200 || response.data == null) {
        messenger.showSnackBar(SnackBar(
          content: Text(l.invoiceDownloadFailed(response.statusCode.toString())),
        ),);
        return;
      }

      final dir = await getTemporaryDirectory();
      final filePath = '${dir.path}/invoice-$invoiceId.pdf';
      await File(filePath).writeAsBytes(response.data as List<int>, flush: true);

      final result = await OpenFilex.open(filePath);
      if (result.type != ResultType.done) {
        messenger.showSnackBar(SnackBar(
          content: Text(l.pdfOpenFailed(result.message)),
        ),);
      }
    } catch (e) {
      messenger.showSnackBar(SnackBar(content: Text(l.invoicePdfError(e.toString()))));
    }
  }
}
