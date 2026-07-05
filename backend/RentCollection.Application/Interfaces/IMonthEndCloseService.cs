namespace RentCollection.Application.Interfaces;

/// <summary>
/// Builds the close-state payload for the Month-End Close checklist
/// (<c>/Reports/MonthEndClose</c>). Extracted from <c>ReportsController</c>
/// so the 9-step aggregator can be exercised by unit tests without
/// going through MVC + auth + DI plumbing.
///
/// Returns <c>object</c> rather than a typed DTO because the Razor page
/// consumes the payload as raw JSON via <c>JsonSerializer</c>; introducing
/// a DTO here would force every view-shape tweak through two layers.
/// Tests assert against <c>JsonElement</c> instead.
/// </summary>
public interface IMonthEndCloseService
{
    /// <summary>
    /// Aggregate the 9 close-state signals for <paramref name="period"/>.
    /// Pass <c>null</c> to default to "now".
    /// </summary>
    /// <param name="period">Period identifier in <c>yyyy-MM</c> format. Invalid input falls back to the current month.</param>
    Task<object> BuildCloseStateAsync(string? period);
}
