import 'dart:io' show SocketException, HandshakeException;

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import '../../core/constants/app_constants.dart';
import 'storage_service.dart';

/// API Service for handling HTTP requests with Dio
class ApiService {
  late final Dio _dio;
  final StorageService _storageService;

  /// Exposed so callers (e.g. token refresh) can persist a renewed token
  /// without taking a separate StorageService dependency.
  StorageService get storage => _storageService;

  /// Invoked once when a request comes back 401 and the token can't be
  /// salvaged. The app wires this in `main.dart` to clear auth state and
  /// boot the user to the login screen, so a mid-session expiry recovers
  /// immediately instead of only on the next cold start.
  void Function()? onUnauthenticated;

  /// Re-entrancy guard: a burst of parallel requests can all 401 at once;
  /// we only want to fire [onUnauthenticated] once. Reset on any success.
  bool _unauthHandled = false;

  ApiService(this._storageService) {
    _dio = Dio(BaseOptions(
      baseUrl: AppConstants.baseUrl,
      connectTimeout: const Duration(milliseconds: AppConstants.connectionTimeout),
      receiveTimeout: const Duration(milliseconds: AppConstants.receiveTimeout),
      // Multipart uploads (transfer proof, maintenance photos) on a slow
      // connection would otherwise hang indefinitely with no send timeout.
      sendTimeout: const Duration(seconds: 60),
      // NOTE: do NOT hardcode 'Content-Type' here. Dio's ImplyContentTypeInterceptor
      // sets application/json for JSON bodies automatically, and crucially sets
      // 'multipart/form-data; boundary=…' for FormData. A global application/json
      // default suppressed that boundary, so multipart uploads (bank-transfer proof,
      // maintenance photos) reached the server as unparseable bodies → 400
      // "InvoiceId is required." Leaving content-type unset per request fixes uploads
      // without changing JSON calls.
      headers: {
        'Accept': 'application/json',
      },
    ),);

    // Auth + 401 handling. On a 401 we clear the token so the next launch
    // forces a fresh login; the actual navigation is left to the UI layer
    // listening on AuthViewModel.
    _dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) async {
        final token = await _storageService.getToken();
        if (token != null && token.isNotEmpty) {
          options.headers['Authorization'] = 'Bearer $token';
        }
        return handler.next(options);
      },
      onResponse: (response, handler) {
        // A successful call means the session is healthy again — re-arm the
        // 401 handler for any future expiry.
        _unauthHandled = false;
        return handler.next(response);
      },
      onError: (error, handler) async {
        if (error.response?.statusCode == 401) {
          final path = error.requestOptions.path;
          final isAuthCall =
              path.contains('/tenant/login') || path.contains('/tenant/refresh');

          // Only drop the stored token if THIS request was sent with it. A 401
          // from an old in-flight request that lands after the user has logged
          // out and back in must not wipe the brand-new token. A login 401
          // (wrong password) carries no token and must not touch storage either.
          if (!isAuthCall) {
            final sent = error.requestOptions.headers['Authorization'] as String?;
            final stored = await _storageService.getToken();
            if (stored != null && sent == 'Bearer $stored') {
              await _storageService.deleteToken();
            }
          }

          // The login + refresh calls own their 401 handling (wrong password,
          // fully-expired token on cold start). Redirecting here would either
          // loop or fight the splash flow, so skip them.
          if (!isAuthCall && !_unauthHandled) {
            _unauthHandled = true;
            onUnauthenticated?.call();
          }
        }
        return handler.next(error);
      },
    ),);

    // Verbose request/response logging is *debug only*. In release builds it
    // would write login bodies (PACI + password) and bearer tokens to logcat.
    if (kDebugMode) {
      _dio.interceptors.add(LogInterceptor(
        responseBody: true,
        requestHeader: false,
        responseHeader: false,
      ),);
    }
  }

  /// GET request
  Future<Response> get(
    String endpoint, {
    Map<String, dynamic>? queryParameters,
  }) async {
    try {
      final response = await _dio.get(
        endpoint,
        queryParameters: queryParameters,
      );
      return response;
    } on DioException catch (e) {
      throw _handleError(e);
    }
  }

  /// POST request
  Future<Response> post(
    String endpoint, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
  }) async {
    try {
      final response = await _dio.post(
        endpoint,
        data: data,
        queryParameters: queryParameters,
      );
      return response;
    } on DioException catch (e) {
      throw _handleError(e);
    }
  }

  /// PUT request
  Future<Response> put(
    String endpoint, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
  }) async {
    try {
      final response = await _dio.put(
        endpoint,
        data: data,
        queryParameters: queryParameters,
      );
      return response;
    } on DioException catch (e) {
      throw _handleError(e);
    }
  }

  /// DELETE request
  Future<Response> delete(
    String endpoint, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
  }) async {
    try {
      final response = await _dio.delete(
        endpoint,
        data: data,
        queryParameters: queryParameters,
      );
      return response;
    } on DioException catch (e) {
      throw _handleError(e);
    }
  }

  /// Upload file with multipart form data
  Future<Response> uploadFile(
    String endpoint, {
    required String filePath,
    required String fieldName,
    Map<String, dynamic>? additionalFields,
  }) async {
    try {
      final formData = FormData.fromMap({
        fieldName: await MultipartFile.fromFile(filePath),
        ...?additionalFields,
      });
      final response = await _dio.post(endpoint, data: formData);
      return response;
    } on DioException catch (e) {
      throw _handleError(e);
    }
  }

  /// Convert Dio exceptions into a typed [ApiException] the UI layer can
  /// match on. Network/HTTP details are deliberately not surfaced — they
  /// either belong in crash reports or in dev logs (see kDebugMode block).
  ApiException _handleError(DioException error) {
    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return ApiException(
          'Connection timeout. Please check your internet connection.',
          kind: ApiErrorKind.timeout,
        );
      case DioExceptionType.connectionError:
        return ApiException(
          'Unable to connect to server. Please check your internet connection.',
          kind: ApiErrorKind.network,
        );
      case DioExceptionType.badResponse:
        final statusCode = error.response?.statusCode ?? 0;
        final responseData = error.response?.data;
        final path = error.requestOptions.path;
        // Different endpoints use message / detail / error / errorMessage /
        // title. Take the first non-empty STRING — a Map or List here (e.g.
        // ASP.NET ProblemDetails `errors`) would otherwise throw a TypeError.
        String? serverMessage;
        if (responseData is Map) {
          for (final key in const ['message', 'detail', 'errorMessage', 'error', 'title']) {
            final v = responseData[key];
            if (v is String && v.trim().isNotEmpty) {
              serverMessage = v;
              break;
            }
          }
        }
        final message = serverMessage ?? 'An error occurred';

        // Anything 5xx (500, 502, 503 e.g. KNET switched off, 504) is a
        // server error — not just 500.
        if (statusCode >= 500) {
          return ApiException('Server error. Please try again later.',
              kind: ApiErrorKind.serverError, serverMessage: serverMessage,);
        }

        switch (statusCode) {
          case 400:
            return ApiException(message, kind: ApiErrorKind.badRequest, serverMessage: serverMessage);
          case 401:
            // A 401 from the LOGIN endpoint means wrong Civil ID / password —
            // not an expired session. Report it as a bad request so the UI
            // shows "invalid credentials", not "session expired".
            if (path.contains('/tenant/login')) {
              return ApiException(message, kind: ApiErrorKind.invalidCredentials, serverMessage: serverMessage);
            }
            return ApiException(
              'Session expired. Please log in again.',
              kind: ApiErrorKind.unauthenticated,
              serverMessage: serverMessage,
            );
          case 403:
            return ApiException('Access denied.', kind: ApiErrorKind.forbidden, serverMessage: serverMessage);
          case 404:
            return ApiException('Resource not found.', kind: ApiErrorKind.notFound, serverMessage: serverMessage);
          case 409:
            // The pay-oldest-first rule returns 409 WITH { oldestInvoiceId,
            // oldestBalance }. Other 409s (e.g. "a KNET payment is already in
            // progress", "cash request already pending") carry no invoice id
            // and are plain conflicts — don't mislabel them as paymentBlocked.
            final oldestId = responseData is Map ? responseData['oldestInvoiceId'] as int? : null;
            final oldestBal = responseData is Map ? (responseData['oldestBalance'] as num?)?.toDouble() : null;
            if (oldestId != null) {
              return ApiException(
                message,
                kind: ApiErrorKind.paymentBlocked,
                oldestInvoiceId: oldestId,
                oldestBalance: oldestBal,
                serverMessage: serverMessage,
              );
            }
            return ApiException(message, kind: ApiErrorKind.conflict, serverMessage: serverMessage);
          case 429:
            return ApiException(
              'Too many attempts — please wait a minute and try again.',
              kind: ApiErrorKind.rateLimited,
              serverMessage: serverMessage,
            );
          default:
            return ApiException(message, serverMessage: serverMessage);
        }
      case DioExceptionType.cancel:
        return ApiException('Request cancelled.', kind: ApiErrorKind.cancelled);
      default:
        // Some socket / TLS handshake failures surface as `unknown` rather than
        // `connectionError`. They're still "no network", and must not log the
        // user out on a cold start.
        final inner = error.error;
        if (inner is SocketException || inner is HandshakeException) {
          return ApiException(
            'Unable to connect to server. Please check your internet connection.',
            kind: ApiErrorKind.network,
          );
        }
        return ApiException('An unexpected error occurred.');
    }
  }
}

/// Typed error categories. UI uses [kind] to decide whether to redirect to
/// login (`unauthenticated`), prompt a retry (`network`/`timeout`), or just
/// show a snackbar.
enum ApiErrorKind {
  timeout,
  network,
  badRequest,
  /// HTTP 401 from the LOGIN endpoint specifically: wrong Civil ID / password.
  /// Distinct from [unauthenticated] (an expired session on an authed call).
  invalidCredentials,
  unauthenticated,
  forbidden,
  notFound,
  /// HTTP 409 that is NOT the pay-oldest-first rule (e.g. "a payment is already
  /// in progress for this invoice"). No oldestInvoiceId attached.
  conflict,
  /// Sprint 26 — server returned 409 because the tenant tried to pay an
  /// invoice that isn't the oldest unpaid one. See [ApiException.oldestInvoiceId]
  /// for the invoice they need to pay first.
  paymentBlocked,
  /// HTTP 429 — Email-test, K-NET, or any other rate-limited endpoint.
  rateLimited,
  serverError,
  cancelled,
  unknown,
}

class ApiException implements Exception {
  final String message;
  final ApiErrorKind kind;

  /// Sprint 26 — set when [kind] == [ApiErrorKind.paymentBlocked]. The ID of
  /// the invoice the tenant must pay first.
  final int? oldestInvoiceId;

  /// Sprint 26 — KWD balance still owed on [oldestInvoiceId].
  final double? oldestBalance;

  /// The raw human-readable text the server sent (English), if any. For logs
  /// and debugging; the UI should localize from [kind] instead of showing this.
  final String? serverMessage;

  ApiException(
    this.message, {
    this.kind = ApiErrorKind.unknown,
    this.oldestInvoiceId,
    this.oldestBalance,
    this.serverMessage,
  });

  @override
  String toString() => message;
}
