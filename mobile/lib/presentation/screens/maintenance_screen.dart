import 'dart:io' show File;

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../core/constants/app_constants.dart';
import '../../core/utils/status_localizer.dart';
import '../../l10n/app_localizations.dart';
import '../viewmodels/auth_viewmodel.dart';
import '../viewmodels/maintenance_viewmodel.dart';
import '../viewmodels/lease_viewmodel.dart';
import '../widgets/gradient_background.dart';
import '../widgets/language_toggle_button.dart';
import '../widgets/maintenance_card.dart';

/// Opens the "New Maintenance Request" bottom sheet. Shared by the
/// Maintenance tab's FAB and the Dashboard's "New Request" action so both
/// entry points behave identically (the dashboard one used to be a dead
/// `/maintenance/create` route that just rebuilt HomeScreen).
void showCreateMaintenanceRequestSheet(BuildContext context) {
  final authViewModel = context.read<AuthViewModel>();
  final leaseViewModel = context.read<LeaseViewModel>();
  final tenantId = authViewModel.tenantId;
  if (tenantId == null) return;

  // Make sure the property/unit dropdown has data to show.
  if (!leaseViewModel.hasLeases) {
    leaseViewModel.loadLeases(tenantId);
  }

  showModalBottomSheet(
    context: context,
    isScrollControlled: true,
    backgroundColor: Colors.transparent,
    builder: (_) => CreateMaintenanceRequestSheet(tenantId: tenantId),
  );
}

/// Maintenance requests screen
class MaintenanceScreen extends StatefulWidget {
  const MaintenanceScreen({super.key});

  @override
  State<MaintenanceScreen> createState() => _MaintenanceScreenState();
}

class _MaintenanceScreenState extends State<MaintenanceScreen> {
  @override
  void initState() {
    super.initState();
    // Defer to after first frame so the view-model's synchronous
    // notifyListeners() doesn't fire mid-build.
    WidgetsBinding.instance.addPostFrameCallback((_) => _loadData());
  }

  Future<void> _loadData() async {
    final authViewModel = context.read<AuthViewModel>();
    final tenantId = authViewModel.tenantId;
    if (tenantId != null) {
      await context.read<MaintenanceViewModel>().loadRequests(tenantId);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(AppLocalizations.of(context)!.maintenanceTitle),
        flexibleSpace: Container(
          decoration: const BoxDecoration(
            gradient: AppTheme.primaryGradient,
          ),
        ),
        actions: [
          const LanguageToggleButton(),
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _loadData,
          ),
        ],
      ),
      body: GradientBackground(
        child: Consumer<MaintenanceViewModel>(
          builder: (context, viewModel, _) {
            if (viewModel.isLoading && !viewModel.hasRequests) {
              return const Center(
                child: CircularProgressIndicator(color: AppTheme.primaryIndigo),
              );
            }

            if (viewModel.state == MaintenanceState.error && !viewModel.hasRequests) {
              return _buildErrorState(viewModel.errorMessage ??
                  AppLocalizations.of(context)!.maintenanceFailedToLoad,);
            }

            if (!viewModel.hasRequests) {
              return _buildEmptyState();
            }

            return RefreshIndicator(
              onRefresh: _loadData,
              color: AppTheme.primaryIndigo,
              child: ListView.builder(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.all(16),
                itemCount: viewModel.requests.length,
                itemBuilder: (context, index) {
                  final request = viewModel.requests[index];
                  return MaintenanceCard(
                    request: request,
                    onTap: () => _showRequestDetails(request),
                  );
                },
              ),
            );
          },
        ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _showCreateRequestDialog,
        backgroundColor: AppTheme.primaryIndigo,
        icon: const Icon(Icons.add),
        label: Text(AppLocalizations.of(context)!.maintenanceNewRequest),
      ),
    );
  }

  void _showRequestDetails(request) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => DraggableScrollableSheet(
        initialChildSize: 0.7,
        maxChildSize: 0.9,
        minChildSize: 0.5,
        builder: (context, scrollController) => Container(
          decoration: const BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
          ),
          child: Column(
            children: [
              Container(
                margin: const EdgeInsets.only(top: 12),
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: AppTheme.borderMedium,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
              Expanded(
                child: SingleChildScrollView(
                  controller: scrollController,
                  padding: const EdgeInsets.all(20),
                  child: MaintenanceDetailCard(request: request),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _showCreateRequestDialog() => showCreateMaintenanceRequestSheet(context);

  Widget _buildErrorState(String message) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.error_outline, size: 64, color: AppTheme.errorRed.withOpacity(0.5)),
            const SizedBox(height: 16),
            Text(
              message,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodyLarge?.copyWith(color: AppTheme.textSecondary),
            ),
            const SizedBox(height: 24),
            ElevatedButton.icon(
              onPressed: _loadData,
              icon: const Icon(Icons.refresh),
              label: Text(AppLocalizations.of(context)!.retry),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildEmptyState() {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.build_rounded, size: 80, color: AppTheme.textMuted.withOpacity(0.3)),
          const SizedBox(height: 16),
          Text(
            AppLocalizations.of(context)!.maintenanceEmptyTitle,
            style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
          const SizedBox(height: 8),
          Text(
            AppLocalizations.of(context)!.maintenanceEmptyHint,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
          const SizedBox(height: 24),
          ElevatedButton.icon(
            onPressed: _showCreateRequestDialog,
            icon: const Icon(Icons.add),
            label: Text(AppLocalizations.of(context)!.maintenanceNewRequest),
          ),
        ],
      ),
    );
  }
}

/// Bottom sheet for creating a new maintenance request
class CreateMaintenanceRequestSheet extends StatefulWidget {
  final int tenantId;

  const CreateMaintenanceRequestSheet({
    super.key,
    required this.tenantId,
  });

  @override
  State<CreateMaintenanceRequestSheet> createState() => _CreateMaintenanceRequestSheetState();
}

class _CreateMaintenanceRequestSheetState extends State<CreateMaintenanceRequestSheet> {
  final _formKey = GlobalKey<FormState>();
  int? _selectedLeaseId;
  String? _selectedCategory;
  String? _selectedPriority;
  final _descriptionController = TextEditingController();

  /// Local file paths picked from camera/gallery. Uploaded after the request
  /// is created (see MaintenanceViewModel.createRequest).
  final List<XFile> _pickedPhotos = [];
  final ImagePicker _picker = ImagePicker();

  @override
  void dispose() {
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _pickFromGallery() async {
    final picked = await _picker.pickMultiImage(maxWidth: 1920, imageQuality: 80);
    if (picked.isEmpty) return;
    setState(() => _pickedPhotos.addAll(picked.take(5 - _pickedPhotos.length)));
  }

  Future<void> _pickFromCamera() async {
    if (_pickedPhotos.length >= 5) return;
    final shot = await _picker.pickImage(source: ImageSource.camera, maxWidth: 1920, imageQuality: 80);
    if (shot == null) return;
    setState(() => _pickedPhotos.add(shot));
  }

  Future<void> _submitRequest() async {
    if (!_formKey.currentState!.validate()) return;

    final maintenanceViewModel = context.read<MaintenanceViewModel>();
    final success = await maintenanceViewModel.createRequest(
      widget.tenantId,
      leaseId: _selectedLeaseId!,
      category: _selectedCategory!,
      priority: _selectedPriority!,
      description: _descriptionController.text,
      photoPaths: _pickedPhotos.map((x) => x.path).toList(),
    );

    if (!mounted) return;
    final l = AppLocalizations.of(context)!;
    // Capture the messenger BEFORE popping — after pop this sheet's context
    // is gone and ScaffoldMessenger.of(context) would be invalid.
    final messenger = ScaffoldMessenger.of(context);

    if (success) {
      Navigator.pop(context);
      messenger.showSnackBar(
        SnackBar(
          content: Text(l.maintenanceSubmitted),
          backgroundColor: AppTheme.successGreen,
        ),
      );
      // Refresh the list
      await maintenanceViewModel.refresh(widget.tenantId);
    } else {
      // Previously a failure was silent — the sheet just stayed open.
      messenger.showSnackBar(
        SnackBar(
          content: Text(maintenanceViewModel.errorMessage ?? l.maintenanceSubmitFailed),
          backgroundColor: AppTheme.errorRed,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return DraggableScrollableSheet(
      initialChildSize: 0.85,
      maxChildSize: 0.95,
      minChildSize: 0.5,
      builder: (context, scrollController) => Container(
        decoration: const BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
        child: Consumer2<LeaseViewModel, MaintenanceViewModel>(
          builder: (context, leaseViewModel, maintenanceViewModel, _) {
            final l = AppLocalizations.of(context)!;
            return Column(
              children: [
                Container(
                  margin: const EdgeInsets.only(top: 12),
                  width: 40,
                  height: 4,
                  decoration: BoxDecoration(
                    color: AppTheme.borderMedium,
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.all(20),
                  child: Row(
                    children: [
                      const Icon(Icons.build, color: AppTheme.primaryIndigo),
                      const SizedBox(width: 12),
                      Text(
                        l.maintenanceCreateTitle,
                        style: Theme.of(context).textTheme.titleLarge?.copyWith(
                              fontWeight: FontWeight.bold,
                            ),
                      ),
                    ],
                  ),
                ),
                const Divider(height: 1),
                Expanded(
                  child: SingleChildScrollView(
                    controller: scrollController,
                    padding: const EdgeInsets.all(20),
                    child: Form(
                      key: _formKey,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          // Property/Unit Selection
                          Text(l.maintenancePropertyUnit, style: Theme.of(context).textTheme.labelLarge),
                          const SizedBox(height: 8),
                          DropdownButtonFormField<int>(
                            value: _selectedLeaseId,
                            decoration: InputDecoration(
                              hintText: l.maintenanceSelectProperty,
                            ),
                            items: leaseViewModel.leases.map((lease) {
                              return DropdownMenuItem(
                                value: lease.leaseId,
                                child: Text('${lease.propertyName} – ${l.labelUnit} ${lease.unitNumber}'),
                              );
                            }).toList(),
                            onChanged: (value) => setState(() => _selectedLeaseId = value),
                            validator: (value) => value == null ? l.maintenanceSelectPropertyError : null,
                          ),
                          const SizedBox(height: 20),

                          // Category Selection
                          Text(l.maintenanceCategory, style: Theme.of(context).textTheme.labelLarge),
                          const SizedBox(height: 8),
                          DropdownButtonFormField<String>(
                            value: _selectedCategory,
                            decoration: InputDecoration(
                              hintText: l.maintenanceSelectCategory,
                            ),
                            items: AppConstants.maintenanceCategories.map((category) {
                              return DropdownMenuItem(
                                value: category, // English API value
                                child: Text(localizeMaintenanceCategory(context, category)),
                              );
                            }).toList(),
                            onChanged: (value) => setState(() => _selectedCategory = value),
                            validator: (value) => value == null ? l.maintenanceSelectCategoryError : null,
                          ),
                          const SizedBox(height: 20),

                          // Priority Selection
                          Text(l.maintenancePriority, style: Theme.of(context).textTheme.labelLarge),
                          const SizedBox(height: 8),
                          DropdownButtonFormField<String>(
                            value: _selectedPriority,
                            decoration: InputDecoration(
                              hintText: l.maintenanceSelectPriority,
                            ),
                            items: AppConstants.maintenancePriorities.map((priority) {
                              return DropdownMenuItem(
                                value: priority, // English API value
                                child: Text(localizeMaintenancePriority(context, priority)),
                              );
                            }).toList(),
                            onChanged: (value) => setState(() => _selectedPriority = value),
                            validator: (value) => value == null ? l.maintenanceSelectPriorityError : null,
                          ),
                          const SizedBox(height: 20),

                          // Description
                          Text(l.maintenanceDescription, style: Theme.of(context).textTheme.labelLarge),
                          const SizedBox(height: 8),
                          TextFormField(
                            controller: _descriptionController,
                            maxLines: 4,
                            decoration: InputDecoration(
                              hintText: l.maintenanceDescriptionHint,
                            ),
                            validator: (value) {
                              if (value == null || value.isEmpty) {
                                return l.maintenanceDescriptionError;
                              }
                              return null;
                            },
                          ),
                          const SizedBox(height: 20),

                          // Photos (optional — up to 5, JPEG/PNG/HEIC/WEBP, 5 MB each).
                          // Helps the maintenance team triage without a site visit.
                          Text(l.maintenancePhotosOptional, style: Theme.of(context).textTheme.labelLarge),
                          const SizedBox(height: 6),
                          Text(
                            l.maintenancePhotosHint,
                            style: Theme.of(context).textTheme.bodySmall?.copyWith(color: AppTheme.textMuted),
                          ),
                          const SizedBox(height: 10),
                          Row(
                            children: [
                              OutlinedButton.icon(
                                onPressed: _pickedPhotos.length >= 5 ? null : _pickFromCamera,
                                icon: const Icon(Icons.photo_camera_outlined, size: 18),
                                label: Text(l.maintenanceCamera),
                              ),
                              const SizedBox(width: 8),
                              OutlinedButton.icon(
                                onPressed: _pickedPhotos.length >= 5 ? null : _pickFromGallery,
                                icon: const Icon(Icons.photo_library_outlined, size: 18),
                                label: Text(l.maintenanceGallery),
                              ),
                              const SizedBox(width: 8),
                              Text('${_pickedPhotos.length}/5',
                                  style: const TextStyle(color: AppTheme.textMuted),),
                            ],
                          ),
                          if (_pickedPhotos.isNotEmpty) ...[
                            const SizedBox(height: 12),
                            SizedBox(
                              height: 90,
                              child: ListView.separated(
                                scrollDirection: Axis.horizontal,
                                itemCount: _pickedPhotos.length,
                                separatorBuilder: (_, __) => const SizedBox(width: 8),
                                itemBuilder: (context, i) {
                                  final x = _pickedPhotos[i];
                                  return Stack(
                                    children: [
                                      ClipRRect(
                                        borderRadius: BorderRadius.circular(10),
                                        child: Image.file(
                                          File(x.path),
                                          width: 90,
                                          height: 90,
                                          fit: BoxFit.cover,
                                        ),
                                      ),
                                      Positioned(
                                        right: -8,
                                        top: -8,
                                        child: IconButton(
                                          icon: const Icon(Icons.cancel, color: Colors.black54),
                                          onPressed: () => setState(() => _pickedPhotos.removeAt(i)),
                                        ),
                                      ),
                                    ],
                                  );
                                },
                              ),
                            ),
                          ],
                          const SizedBox(height: 32),

                          // Submit Button
                          SizedBox(
                            width: double.infinity,
                            child: ElevatedButton.icon(
                              onPressed: maintenanceViewModel.isSubmitting ? null : _submitRequest,
                              icon: maintenanceViewModel.isSubmitting
                                  ? const SizedBox(
                                      width: 20,
                                      height: 20,
                                      child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                        color: Colors.white,
                                      ),
                                    )
                                  : const Icon(Icons.send),
                              label: Text(maintenanceViewModel.isSubmitting
                                  ? l.maintenanceSubmitting
                                  : l.maintenanceSubmitRequest,),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}
