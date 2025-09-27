namespace MarketDataExcelUpdater.Tests.TestDoubles;

/// <summary>
/// Deterministic clock for driving time-based logic in tests.
/// </summary>
public sealed class TestClock
{
    private DateTimeOffset _now;

    public TestClock(DateTimeOffset start) => _now = start;

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
