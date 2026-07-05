import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:provider/provider.dart';

import 'core/theme/app_theme.dart';
import 'core/locale_provider.dart';
import 'core/version_gate.dart';
import 'core/constants/app_constants.dart';
import 'l10n/app_localizations.dart';
import 'data/services/storage_service.dart';
import 'data/services/api_service.dart';
import 'data/services/tenant_api_service.dart';
import 'data/services/fcm_service.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'presentation/viewmodels/auth_viewmodel.dart';
import 'presentation/viewmodels/dashboard_viewmodel.dart';
import 'presentation/viewmodels/invoice_viewmodel.dart';
import 'presentation/viewmodels/lease_viewmodel.dart';
import 'presentation/viewmodels/payment_viewmodel.dart';
import 'presentation/viewmodels/maintenance_viewmodel.dart';
import 'presentation/viewmodels/profile_viewmodel.dart';
import 'presentation/screens/login_screen.dart';
import 'presentation/screens/home_screen.dart';
import 'presentation/screens/make_payment_screen.dart';
import 'presentation/screens/payment_result_screen.dart';
import 'presentation/screens/force_update_screen.dart';

/// Single funnel for unhandled errors. Today it just logs in debug builds
/// and swallows in release; wire Sentry/Crashlytics here in Phase 4 of the
/// next pass:
///
///   await Sentry.captureException(error, stackTrace: stack);
void _reportUncaughtError(Object error, StackTrace stack) {
  if (kDebugMode) {
    // ignore: avoid_print
    print('Uncaught error: $error\n$stack');
  }
}

void main() {
  // Wrap everything in runZonedGuarded so async errors thrown outside a
  // Flutter frame (timers, isolates, microtasks) also reach the funnel.
  runZonedGuarded<Future<void>>(() async {
    WidgetsFlutterBinding.ensureInitialized();

    // Load date-formatting symbols for all locales so AppDateUtils can format
    // Arabic month/day names (with Western digits) once the user picks Arabic.
    await initializeDateFormatting();

    // Funnel: any uncaught widget/build error.
    FlutterError.onError = (FlutterErrorDetails details) {
      FlutterError.presentError(details);
      _reportUncaughtError(details.exception, details.stack ?? StackTrace.current);
    };

    // Funnel: platform-level errors (e.g. plugin failures).
    PlatformDispatcher.instance.onError = (error, stack) {
      _reportUncaughtError(error, stack);
      return true;
    };

    await SystemChrome.setPreferredOrientations([
      DeviceOrientation.portraitUp,
      DeviceOrientation.portraitDown,
    ]);

    SystemChrome.setSystemUIOverlayStyle(const SystemUiOverlayStyle(
      statusBarColor: Colors.transparent,
      statusBarIconBrightness: Brightness.light,
    ));

    final storageService = StorageService();
    await storageService.init();

    final apiService = ApiService(storageService);
    final tenantApiService = TenantApiService(apiService);

    // Sprint 26 — FCM. Registering the background message handler MUST
    // happen before runApp so the OS can wake a fresh isolate when a
    // push arrives while the app isn't running. Initialize() runs the
    // foreground permission request + token fetch later (in FcmService).
    FirebaseMessaging.onBackgroundMessage(firebaseMessagingBackgroundHandler);
    final fcmService = FcmService(apiService);
    // Fire-and-forget; initialize() catches its own errors so it can never
    // crash startup (e.g. placeholder google-services.json with no project).
    unawaited(fcmService.initialize());

    // Global navigator so the Dio 401 handler can boot the user to /login
    // from outside the widget tree.
    final navigatorKey = GlobalKey<NavigatorState>();
    apiService.onUnauthenticated = () {
      final ctx = navigatorKey.currentContext;
      if (ctx != null) {
        try {
          ctx.read<AuthViewModel>().logout();
        } catch (_) {
          // Provider not mounted yet (e.g. during splash) — clearing the
          // token in the interceptor is enough; splash will route to login.
        }
      }
      navigatorKey.currentState?.pushNamedAndRemoveUntil(
        '/login',
        (route) => false,
      );
    };

    runApp(ViijarTenantApp(
      storageService: storageService,
      tenantApiService: tenantApiService,
      fcmService: fcmService,
      navigatorKey: navigatorKey,
    ));
  }, _reportUncaughtError);
}

class ViijarTenantApp extends StatelessWidget {
  final StorageService storageService;
  final TenantApiService tenantApiService;
  final FcmService fcmService;
  final GlobalKey<NavigatorState> navigatorKey;

  const ViijarTenantApp({
    super.key,
    required this.storageService,
    required this.tenantApiService,
    required this.fcmService,
    required this.navigatorKey,
  });

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        // Plain (non-notifier) service — SplashScreen reads it for the
        // version-gate check before any view-model is needed.
        Provider<TenantApiService>.value(value: tenantApiService),
        // Sprint 26 — FCM push wrapper. Read from anywhere that needs to
        // register the device token (Login screen on success, Splash on
        // resume-with-saved-session).
        Provider<FcmService>.value(value: fcmService),
        // App language (persisted; null = follow device locale)
        ChangeNotifierProvider(
          create: (_) => LocaleProvider(storageService),
        ),
        // Auth ViewModel
        ChangeNotifierProvider(
          create: (_) => AuthViewModel(tenantApiService, storageService),
        ),
        // Dashboard ViewModel
        ChangeNotifierProvider(
          create: (_) => DashboardViewModel(tenantApiService),
        ),
        // Lease ViewModel
        ChangeNotifierProvider(
          create: (_) => LeaseViewModel(tenantApiService),
        ),
        // Payment ViewModel
        ChangeNotifierProvider(
          create: (_) => PaymentViewModel(tenantApiService),
        ),
        // Invoice ViewModel — backs the Invoices tab + invoice detail screen.
        ChangeNotifierProvider(
          create: (_) => InvoiceViewModel(tenantApiService),
        ),
        // Maintenance ViewModel
        ChangeNotifierProvider(
          create: (_) => MaintenanceViewModel(tenantApiService),
        ),
        // Profile ViewModel
        ChangeNotifierProvider(
          create: (_) => ProfileViewModel(tenantApiService),
        ),
      ],
      child: Consumer<LocaleProvider>(
        builder: (context, localeProvider, _) => MaterialApp(
          title: 'VIIJAR Tenant',
          navigatorKey: navigatorKey,
          debugShowCheckedModeBanner: false,
          theme: AppTheme.lightTheme,
          darkTheme: AppTheme.darkTheme,
          themeMode: ThemeMode.light,
          // Localization — Material + Cupertino + Widgets delegates so all
          // built-in widgets switch to Arabic (and RTL) too. `locale` is
          // driven by the in-app switcher; null = follow the device locale.
          locale: localeProvider.locale,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: const SplashScreen(),
          routes: {
          '/login': (context) => const LoginScreen(),
          '/home': (context) => const HomeScreen(),
          // Payment is now invoice-driven: callers push '/payments/make' with
          // the selected InvoiceModel as the route argument.
          '/payments/make': (context) => const MakePaymentScreen(),
          '/payments/result': (context) => const PaymentResultScreen(),
          // '/payments/history' removed — payment history lives under each
          // invoice's detail screen now.
          // '/maintenance/create' removed — it mapped to HomeScreen (a no-op).
          // The create sheet is opened directly via
          // showCreateMaintenanceRequestSheet() from the Maintenance tab FAB
          // and the Dashboard "New Request" action.
          },
        ),
      ),
    );
  }
}

/// Splash screen that checks auth status
class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> with SingleTickerProviderStateMixin {
  late AnimationController _animationController;
  late Animation<double> _scaleAnimation;
  late Animation<double> _fadeAnimation;

  @override
  void initState() {
    super.initState();
    _animationController = AnimationController(
      duration: const Duration(milliseconds: 1500),
      vsync: this,
    );
    _scaleAnimation = Tween<double>(begin: 0.5, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.elasticOut),
    );
    _fadeAnimation = Tween<double>(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeIn),
    );
    _animationController.forward();
    _checkAuthStatus();
  }

  @override
  void dispose() {
    _animationController.dispose();
    super.dispose();
  }

  Future<void> _checkAuthStatus() async {
    // Wait for animation
    await Future.delayed(const Duration(milliseconds: 2000));

    if (!mounted) return;

    // Force-update gate runs before auth. Fail-open: if the endpoint is
    // unreachable or returns nothing, fall through to the normal flow — a
    // network blip must never lock the user out.
    try {
      final info = await context
          .read<TenantApiService>()
          .getAppVersionInfo(platform: 'android');
      if (!mounted) return;
      if (info != null) {
        final status = VersionGate.evaluate(
          current: AppConstants.appVersion,
          minSupported: info.minSupportedVersion,
          latest: info.latestVersion,
        );
        if (status == UpdateStatus.required) {
          Navigator.of(context).pushReplacement(
            MaterialPageRoute(
              builder: (_) => ForceUpdateScreen(storeUrl: info.storeUrl),
            ),
          );
          return;
        }
      }
    } catch (_) {
      // fail-open
    }

    if (!mounted) return;

    final authViewModel = context.read<AuthViewModel>();
    await authViewModel.checkAuthStatus();

    if (!mounted) return;

    if (authViewModel.isAuthenticated) {
      // Sprint 26 — re-register the FCM device token on each warm start
      // so the server stays current on which devices belong to this
      // tenant. FCM tokens rotate rarely but do; this catches it.
      final tenantId = authViewModel.tenantId;
      if (tenantId != null) {
        // ignore: discarded_futures
        context.read<FcmService>().registerWithServer(tenantId);
      }
      Navigator.of(context).pushReplacementNamed('/home');
    } else {
      Navigator.of(context).pushReplacementNamed('/login');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Container(
        decoration: const BoxDecoration(
          gradient: AppTheme.primaryGradient,
        ),
        child: Center(
          child: AnimatedBuilder(
            animation: _animationController,
            builder: (context, child) {
              return FadeTransition(
                opacity: _fadeAnimation,
                child: ScaleTransition(
                  scale: _scaleAnimation,
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      // Logo
                      Container(
                        width: 120,
                        height: 120,
                        decoration: BoxDecoration(
                          color: Colors.white,
                          borderRadius: BorderRadius.circular(30),
                          boxShadow: [
                            BoxShadow(
                              color: Colors.black.withOpacity(0.2),
                              blurRadius: 30,
                              offset: const Offset(0, 10),
                            ),
                          ],
                        ),
                        child: const Icon(
                          Icons.apartment_rounded,
                          size: 60,
                          color: AppTheme.primaryIndigo,
                        ),
                      ),
                      const SizedBox(height: 24),
                      // App Name
                      const Text(
                        'VIIJAR',
                        style: TextStyle(
                          color: Colors.white,
                          fontSize: 40,
                          fontWeight: FontWeight.bold,
                          letterSpacing: 2,
                        ),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        AppLocalizations.of(context)!.loginTenantPortal,
                        style: const TextStyle(
                          color: Colors.white70,
                          fontSize: 18,
                          fontWeight: FontWeight.w500,
                        ),
                      ),
                      const SizedBox(height: 48),
                      // Loading indicator
                      const SizedBox(
                        width: 24,
                        height: 24,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                        ),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
