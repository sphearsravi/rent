import 'package:flutter/widgets.dart';
import '../../data/services/api_service.dart';
import '../../l10n/app_localizations.dart';

/// Domain-level failure surfaced to the UI.
///
/// View-models should never store raw `Exception` strings (which often contain
/// stack info or PII). Instead they convert caught errors into a `Failure`
/// using [Failure.fromException], and screens call [Failure.localizedMessage]
/// to render the user-facing text in the active locale.
sealed class Failure {
  const Failure();

  /// User-facing message in the caller's locale.
  String localizedMessage(BuildContext context);

  /// Whether the UI should redirect the user back to the login screen.
  bool get requiresReauth => false;

  /// Map a caught error into a Failure. Unknown errors map to [UnknownFailure];
  /// the original exception is preserved for crash-reporter hand-off.
  static Failure fromException(Object error, [StackTrace? stack]) {
    if (error is ApiException) {
      switch (error.kind) {
        case ApiErrorKind.timeout:
          return const TimeoutFailure();
        case ApiErrorKind.network:
          return const NetworkFailure();
        case ApiErrorKind.unauthenticated:
          return const UnauthenticatedFailure();
        case ApiErrorKind.forbidden:
          return const ForbiddenFailure();
        case ApiErrorKind.notFound:
          return const NotFoundFailure();
        case ApiErrorKind.serverError:
          return const ServerFailure();
        case ApiErrorKind.badRequest:
          return BadRequestFailure(error.message);
        case ApiErrorKind.paymentBlocked:
          // 409 — server message tells the tenant which invoice to pay first.
          return BadRequestFailure(error.message);
        case ApiErrorKind.rateLimited:
          // 429 — server message carries the retry guidance.
          return BadRequestFailure(error.message);
        case ApiErrorKind.cancelled:
          return const CancelledFailure();
        case ApiErrorKind.unknown:
          return UnknownFailure(error, stack);
      }
    }
    return UnknownFailure(error, stack);
  }
}

class NetworkFailure extends Failure {
  const NetworkFailure();
  @override
  String localizedMessage(BuildContext context) =>
      AppLocalizations.of(context)?.errorNetwork ?? 'Network error';
}

class TimeoutFailure extends Failure {
  const TimeoutFailure();
  @override
  String localizedMessage(BuildContext context) =>
      AppLocalizations.of(context)?.errorTimeout ?? 'Request timed out';
}

class UnauthenticatedFailure extends Failure {
  const UnauthenticatedFailure();
  @override
  bool get requiresReauth => true;
  @override
  String localizedMessage(BuildContext context) =>
      AppLocalizations.of(context)?.errorSessionExpired ?? 'Session expired';
}

class ForbiddenFailure extends Failure {
  const ForbiddenFailure();
  @override
  String localizedMessage(BuildContext context) =>
      AppLocalizations.of(context)?.errorForbidden ?? 'Forbidden';
}

class NotFoundFailure extends Failure {
  const NotFoundFailure();
  @override
  String localizedMessage(BuildContext context) =>
      AppLocalizations.of(context)?.errorNotFound ?? 'Not found';
}

class ServerFailure extends Failure {
  const ServerFailure();
  @override
  String localizedMessage(BuildContext context) =>
      AppLocalizations.of(context)?.errorServer ?? 'Server error';
}

/// 400 from the server — message comes from the backend (already user-facing).
class BadRequestFailure extends Failure {
  const BadRequestFailure(this.message);
  final String message;
  @override
  String localizedMessage(BuildContext context) => message;
}

class CancelledFailure extends Failure {
  const CancelledFailure();
  @override
  String localizedMessage(BuildContext context) => '';
}

/// Catch-all. Preserves the original throwable so the crash reporter
/// (Sentry/Crashlytics) can capture it without it bleeding into the UI.
class UnknownFailure extends Failure {
  const UnknownFailure(this.error, this.stackTrace);
  final Object error;
  final StackTrace? stackTrace;
  @override
  String localizedMessage(BuildContext context) =>
      AppLocalizations.of(context)?.errorGeneric ?? 'Something went wrong';
}
