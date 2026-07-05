namespace RentCollection.Application.Interfaces;

/// <summary>
/// Composes <see cref="ExecutiveDashboardPdfModel"/> for a given period.
/// Used by both the on-demand `/Reports/ExecutiveDashboardPdf` controller
/// action and the monthly Hangfire email job — they were duplicating a
/// dozen aggregations before this lived here.
/// </summary>
public interface IExecutiveDashboardComposer
{
    Task<ExecutiveDashboardPdfModel> BuildAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}
