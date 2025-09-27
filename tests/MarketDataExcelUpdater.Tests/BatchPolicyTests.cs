using FluentAssertions;
using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Tests.TestDoubles;

namespace MarketDataExcelUpdater.Tests;

public sealed class BatchPolicyTests
{
    private readonly TestClock _clock = new(DateTimeOffset.UtcNow);

    private static Quote CreateTestQuote(string symbol, DateTime timestamp) =>
        new(null, null, null, null, 100.0m, 1.0m, null, null, null, null, null, null, null, timestamp);

    [Fact]
    public void Should_not_flush_before_count_threshold()
    {
        var policy = new BatchPolicy(maxQuoteCount: 3, maxTimespan: TimeSpan.FromMinutes(1));
        var quote = CreateTestQuote("GGAL", _clock.UtcNow.DateTime);

        var result1 = policy.ShouldFlush(quote, _clock.UtcNow);
        var result2 = policy.ShouldFlush(quote, _clock.UtcNow);

        result1.Should().BeFalse();
        result2.Should().BeFalse();
    }

    [Fact]
    public void Should_flush_when_count_threshold_reached()
    {
        var policy = new BatchPolicy(maxQuoteCount: 2, maxTimespan: TimeSpan.FromMinutes(1));
        var quote = CreateTestQuote("YPFD", _clock.UtcNow.DateTime);

        policy.ShouldFlush(quote, _clock.UtcNow); // First quote
        var shouldFlush = policy.ShouldFlush(quote, _clock.UtcNow); // Second quote

        shouldFlush.Should().BeTrue();
    }

    [Fact]
    public void Should_flush_when_time_threshold_exceeded()
    {
        var policy = new BatchPolicy(maxQuoteCount: 100, maxTimespan: TimeSpan.FromSeconds(5));
        var quote = CreateTestQuote("PAMP", _clock.UtcNow.DateTime);

        policy.ShouldFlush(quote, _clock.UtcNow); // Start the batch timer
        _clock.Advance(TimeSpan.FromSeconds(6));

        var shouldFlush = policy.ShouldFlush(quote, _clock.UtcNow);

        shouldFlush.Should().BeTrue();
    }

    [Fact]
    public void Should_not_flush_before_time_threshold()
    {
        var policy = new BatchPolicy(maxQuoteCount: 100, maxTimespan: TimeSpan.FromSeconds(10));
        var quote = CreateTestQuote("ALUA", _clock.UtcNow.DateTime);

        policy.ShouldFlush(quote, _clock.UtcNow);
        _clock.Advance(TimeSpan.FromSeconds(5)); // Half the threshold

        var shouldFlush = policy.ShouldFlush(quote, _clock.UtcNow);

        shouldFlush.Should().BeFalse();
    }

    [Fact]
    public void Reset_clears_counters_and_starts_fresh()
    {
        var policy = new BatchPolicy(maxQuoteCount: 3, maxTimespan: TimeSpan.FromSeconds(10));
        var quote = CreateTestQuote("TXAR", _clock.UtcNow.DateTime);

        // Add some quotes but don't reach threshold
        policy.ShouldFlush(quote, _clock.UtcNow);
        policy.ShouldFlush(quote, _clock.UtcNow);

        // Reset and verify we start counting from zero again
        policy.Reset();
        var shouldFlushAfterReset = policy.ShouldFlush(quote, _clock.UtcNow);

        shouldFlushAfterReset.Should().BeFalse();
    }

    [Fact]
    public void Time_threshold_measured_from_first_quote_in_batch()
    {
        var policy = new BatchPolicy(maxQuoteCount: 100, maxTimespan: TimeSpan.FromSeconds(5));
        var quote = CreateTestQuote("COME", _clock.UtcNow.DateTime);

        // First quote starts the timer
        policy.ShouldFlush(quote, _clock.UtcNow);
        _clock.Advance(TimeSpan.FromSeconds(3));

        // Second quote doesn't restart timer
        policy.ShouldFlush(quote, _clock.UtcNow);
        _clock.Advance(TimeSpan.FromSeconds(3)); // Total: 6 seconds from first quote

        var shouldFlush = policy.ShouldFlush(quote, _clock.UtcNow);

        shouldFlush.Should().BeTrue();
    }
}