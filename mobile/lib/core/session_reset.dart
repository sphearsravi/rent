import 'package:flutter/widgets.dart';
import 'package:provider/provider.dart';

import '../presentation/viewmodels/dashboard_viewmodel.dart';
import '../presentation/viewmodels/invoice_viewmodel.dart';
import '../presentation/viewmodels/lease_viewmodel.dart';
import '../presentation/viewmodels/maintenance_viewmodel.dart';
import '../presentation/viewmodels/payment_viewmodel.dart';
import '../presentation/viewmodels/profile_viewmodel.dart';

/// Wipe every data view-model when a session ends.
///
/// The view-models are app-level singletons (created once in main.dart), so
/// without this the NEXT tenant to log in on the same device briefly saw the
/// previous tenant's dashboard, invoices and profile — and kept seeing them if
/// their own refresh failed, because screens hide errors while `hasData`.
void resetSessionState(BuildContext context) {
  void safe(void Function() f) {
    try {
      f();
    } catch (_) {
      // Provider not mounted in this subtree (e.g. during splash) — skip.
    }
  }

  safe(() => context.read<DashboardViewModel>().reset());
  safe(() => context.read<InvoiceViewModel>().reset());
  safe(() => context.read<LeaseViewModel>().reset());
  safe(() => context.read<MaintenanceViewModel>().reset());
  safe(() => context.read<PaymentViewModel>().reset());
  safe(() => context.read<ProfileViewModel>().reset());
}
