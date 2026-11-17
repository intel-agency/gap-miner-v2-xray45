using Microsoft.Extensions.Logging;

namespace GapMiner.ScraperWorker.Tests.TestSupport;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that records every log event's
/// structured state — the observation point for the job's failure-logging
/// acceptance criterion: the failure event must carry the
/// <c>JobId</c>/<c>TargetId</c>/<c>FailureReason</c> properties, which the
/// default test logger would silently discard.
/// </summary>
internal sealed class SpyLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>One captured log event.</summary>
    /// <param name="Level">The severity the event was logged at.</param>
    /// <param name="State">The structured state, as key/value pairs.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The attached exception, when there was one.</param>
    public sealed record Entry(
        LogLevel Level,
        IReadOnlyDictionary<string, object?> State,
        string Message,
        Exception? Exception);

    public List<Entry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel)
        => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<string, object?> { ["state"] = state };
        Entries.Add(new Entry(logLevel, values, formatter(state, exception), exception));
    }
}
