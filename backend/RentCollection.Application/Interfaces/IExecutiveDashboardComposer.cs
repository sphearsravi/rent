namespace RentCollection.Application.Interfaces;

/// <summary>
/// Composes <see cref="ExecutiveDashboardPdfModel"/> for a given period.
/// Used by both the on-demand `/Reports/ExecutiveDashboardPdf` controller
/// action and the monthly Hangfire email job — they were duplicating a
/// dozen aggregations before this lived here.
/// </summary>
public interface IExecutiveDashboardComposer
{
    /// <summary>System-wide dashboard (SuperAdmin view / ops mailing list).</summary>
    Task<ExecutiveDashboardPdfModel> BuildAsync(DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>
    /// Company-scoped dashboard. <paramref name="companyId"/> = null is the
    /// system-wide view. Kept as a separate overload (rather than an optional
    /// parameter) so existing callers and Moq setups on the 3-arg shape keep
    /// compiling — expression trees reject optional-argument calls.
    /// </summary>
    Task<ExecutiveDashboardPdfModel> BuildAsync(DateTime? from, DateTime? to, int? companyId, CancellationToken ct = default);
}
