using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace GapMiner.Infrastructure.Queueing;

/// <summary>
/// <see cref="IJobQueue{TCommand}"/> backed by a StackExchange.Redis list
/// (plan T-1.4): FIFO via RPUSH (tail) / LPOP (head). Commands are stored as
/// JSON serialized with <see cref="System.Text.Json"/> using camelCase property
/// names and null values ignored.
/// </summary>
/// <remarks>
/// <see cref="DequeueAsync(System.Threading.CancellationToken)"/> implements the
/// BRPOP pattern as polling: it retries LPOP every
/// <see cref="DefaultPollInterval"/> until a command arrives or the configured
/// dequeue timeout elapses. A literal BRPOP would pop the tail an RPUSH just
/// pushed — LIFO, not FIFO — so the head is polled instead.
/// </remarks>
public sealed class RedisJobQueue<TCommand> : IJobQueue<TCommand> where TCommand : class
{
    /// <summary>The dequeue timeout used when none is configured.</summary>
    public static readonly TimeSpan DefaultDequeueTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The LPOP retry interval used when none is configured.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IConnectionMultiplexer redis;
    private readonly string queueKey;
    private readonly TimeSpan dequeueTimeout;
    private readonly TimeSpan pollInterval;

    /// <summary>
    /// Initializes the queue over the given Redis connection and list key.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="queueKey">
    /// The list key, by convention <c>gapminer:{domain}:{action}</c> (e.g.
    /// <c>gapminer:scrape:pending</c>).
    /// </param>
    /// <param name="dequeueTimeout">
    /// How long <see cref="DequeueAsync(System.Threading.CancellationToken)"/>
    /// blocks on an empty queue before returning <see langword="null"/>;
    /// defaults to <see cref="DefaultDequeueTimeout"/>.
    /// </param>
    /// <param name="pollInterval">
    /// The interval between LPOP retries while blocking; defaults to
    /// <see cref="DefaultPollInterval"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="redis"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="queueKey"/> is null, empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dequeueTimeout"/> or <paramref name="pollInterval"/> is
    /// zero or negative.
    /// </exception>
    public RedisJobQueue(
        IConnectionMultiplexer redis,
        string queueKey,
        TimeSpan? dequeueTimeout = null,
        TimeSpan? pollInterval = null)
    {
        this.redis = redis ?? throw new ArgumentNullException(nameof(redis));
        if (string.IsNullOrWhiteSpace(queueKey))
        {
            throw new ArgumentException("Queue key must not be null, empty or whitespace.", nameof(queueKey));
        }

        this.queueKey = queueKey;
        this.dequeueTimeout = dequeueTimeout ?? DefaultDequeueTimeout;
        this.pollInterval = pollInterval ?? DefaultPollInterval;

        if (this.dequeueTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dequeueTimeout), "Dequeue timeout must be positive.");
        }

        if (this.pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be positive.");
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// <paramref name="command"/> is <see langword="null"/>.
    /// </exception>
    public async Task EnqueueAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(command, SerializerOptions);
        var database = redis.GetDatabase();

        // StackExchange.Redis 2.13 exposes no CancellationToken overloads, so the
        // wait is abandoned via WaitAsync; the command itself still applies server-side.
        await database.ListRightPushAsync(queueKey, json).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TCommand?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var database = redis.GetDatabase();
        var startedAt = Stopwatch.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = await database.ListLeftPopAsync(queueKey).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (value.HasValue)
            {
                return JsonSerializer.Deserialize<TCommand>(value.ToString(), SerializerOptions);
            }

            var remaining = dequeueTimeout - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            await Task.Delay(pollInterval < remaining ? pollInterval : remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<long> LengthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = redis.GetDatabase();

        return database.ListLengthAsync(queueKey).WaitAsync(cancellationToken);
    }
}
