namespace RentCollection.Application.Interfaces;

/// <summary>
/// Test seam over <c>DateTime.UtcNow</c>. Lets unit tests pin "now" to a
/// specific instant so calendar-driven behaviour (MEC aggregator, recurring
/// jobs, lease lifecycle flips) is provable without touching the system clock.
///
/// Production wiring uses <see cref="SystemClock"/>, which delegates to
/// <c>DateTime.UtcNow</c>; tests use <c>FixedClock</c> (in the test project).
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime Today { get; }
}

/// <summary>
/// Production implementation — straight passthrough to <c>DateTime.UtcNow</c>.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Today => DateTime.UtcNow.Date;
}
