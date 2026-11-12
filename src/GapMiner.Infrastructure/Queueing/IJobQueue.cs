namespace GapMiner.Infrastructure.Queueing;

/// <summary>
/// The generic FIFO job queue every GapMiner worker communicates through
/// (plan T-1.4). Commands are opaque payloads to the queue: the implementation
/// serializes them on <see cref="EnqueueAsync"/> and deserializes them on
/// <see cref="DequeueAsync"/>.
/// </summary>
/// <typeparam name="TCommand">The command message type the queue carries.</typeparam>
/// <remarks>
/// Queue keys follow the naming convention in the development plan (§6):
/// <c>gapminer:{domain}:{action}</c>, e.g. <c>gapminer:scrape:pending</c>.
/// </remarks>
public interface IJobQueue<TCommand> where TCommand : class
{
    /// <summary>
    /// Appends a command to the tail of the queue.
    /// </summary>
    /// <param name="command">The command to enqueue.</param>
    /// <param name="cancellationToken">Propagates cancellation of the operation.</param>
    /// <returns>A task that completes when the command is enqueued.</returns>
    Task EnqueueAsync(TCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes and returns the command at the head of the queue, blocking while
    /// the queue is empty for up to the configured dequeue timeout.
    /// </summary>
    /// <param name="cancellationToken">Propagates cancellation of the operation.</param>
    /// <returns>
    /// The dequeued command, or <see langword="null"/> when the dequeue timeout
    /// elapsed on an empty queue.
    /// </returns>
    Task<TCommand?> DequeueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of commands currently waiting in the queue.
    /// </summary>
    /// <param name="cancellationToken">Propagates cancellation of the operation.</param>
    /// <returns>The queue length.</returns>
    Task<long> LengthAsync(CancellationToken cancellationToken = default);
}
