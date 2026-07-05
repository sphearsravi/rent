using RentCollection.Application.Interfaces;

namespace RentCollection.Tests.Helpers;

/// <summary>
/// Test double for <see cref="IClock"/>. Pins "now" to a specific instant so
/// calendar-driven assertions (MEC aggregator, recurring-job idempotency,
/// lease-expiry windows) are reproducible.
///
/// Construct with a <see cref="DateTime"/>; call <see cref="Advance"/> in
/// time-travel tests to simulate day/month progression.
/// </summary>
public sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; private set; }
    public DateTime Today => UtcNow.Date;

    public FixedClock(DateTime now)
    {
        UtcNow = DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    public void SetUtcNow(DateTime now) => UtcNow = DateTime.SpecifyKind(now, DateTimeKind.Utc);
}
