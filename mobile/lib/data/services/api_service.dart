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
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
      },
    ));

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
          // Token is invalid/expired. Drop it so isLoggedIn() returns false.
          await _storageService.deleteToken();

          // The login + refresh calls own their 401 handling (wrong password,
          // fully-expired token on cold start). Redirecting here would either
          // loop or fight the splash flow, so skip them.
          final path = error.requestOptions.path;
          final isAuthCall =
              path.contains('/tenant/login') || path.contains('/tenant/refresh');
          if (!isAuthCall && !_unauthHandled) {
            _unauthHandled = true;
            onUnauthenticated?.call();
          }
        }
        return handler.next(error);
      },
    ));

    // Verbose request/response logging is *debug only*. In release builds it
    // would write login bodies (PACI + password) and bearer tokens to logcat.
    if (kDebugMode) {
      _dio.interceptors.add(LogInterceptor(
        // Skip request bodies entirely so passwords never appear, even in dev.
        requestBody: false,
        responseBody: true,
        requestHeader: false,
        responseHeader: false,
        error: true,
      ));
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
        final statusCode = error.response?.statusCode;
        final responseData = error.response?.data;
        // Server uses both "message" and "detail" depending on endpoint;
        // accept either so we don't drop useful explanation text.
        final message = (responseData is Map
                ? (responseData['message'] ?? responseData['detail'] ?? responseData['error'])
                : null) ??
            'An error occurred';
        switch (statusCode) {
          case 400:
            return ApiException(message, kind: ApiErrorKind.badRequest);
          case 401:
            return ApiException(
              'Session expired. Please log in again.',
              kind: ApiErrorKind.unauthenticated,
            );
          case 403:
            return ApiException('Access denied.', kind: ApiErrorKind.forbidden);
          case 404:
            return ApiException('Resource not found.', kind: ApiErrorKind.notFound);
          case 409:
            // Sprint 26 — server enforces "pay oldest invoice first"
            // (Sprint 24). The K-NET init + bank-transfer upload endpoints
            // return 409 with { oldestInvoiceId, oldestBalance } when the
            // tenant tries to pay anything that isn't their oldest unpaid
            // invoice. Surface those fields so the UI can route the user
            // to the right invoice.
            final oldestId = responseData is Map ? responseData['oldestInvoiceId'] as int? : null;
            final oldestBal = responseData is Map ? (responseData['oldestBalance'] as num?)?.toDouble() : null;
            return ApiException(
              message,
              kind: ApiErrorKind.paymentBlocked,
              oldestInvoiceId: oldestId,
              oldestBalance: oldestBal,
            );
          case 429:
            return ApiException(
              'Too many attempts — please wait a minute and try again.',
              kind: ApiErrorKind.rateLimited,
            );
          case 500:
            return ApiException('Server error. Please try again later.', kind: ApiErrorKind.serverError);
          default:
            return ApiException(message, kind: ApiErrorKind.unknown);
        }
      case DioExceptionType.cancel:
        return ApiException('Request cancelled.', kind: ApiErrorKind.cancelled);
      default:
        return ApiException('An unexpected error occurred.', kind: ApiErrorKind.unknown);
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
  unauthenticated,
  forbidden,
  notFound,
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

  ApiException(
    this.message, {
    this.kind = ApiErrorKind.unknown,
    this.oldestInvoiceId,
    this.oldestBalance,
  });

  @override
  String toString() => message;
}
