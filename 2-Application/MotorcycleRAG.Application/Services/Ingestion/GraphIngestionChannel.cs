using System.Threading.Channels;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Singleton-backed bounded channel that decouples graph ingestion producers
/// (HTTP request scopes) from the single consumer (<see cref="GraphIngestionBackgroundService"/>).
/// </summary>
/// <remarks>
/// <para>
/// This type exists specifically because <see cref="IngestionJobService"/> is registered as a
/// <c>Scoped</c> service. A channel declared as an instance field on a scoped service would not
/// be shared across request scopes, so the producing HTTP request and the consuming hosted
/// service would never observe the same channel instance. Promoting the channel to a dedicated
/// singleton gives every writer and the single reader a common, application-lifetime queue.
/// </para>
/// <para>
/// <see cref="BoundedChannelOptions.SingleReader"/> is set because exactly one consumer
/// (<see cref="GraphIngestionBackgroundService"/>) reads; <see cref="BoundedChannelOptions.SingleWriter"/>
/// is <c>false</c> because multiple concurrent HTTP requests may enqueue jobs.
/// </para>
/// <para>
/// <see cref="BoundedChannelFullMode.Wait"/> applies backpressure: when the queue is full, the
/// producing HTTP request awaits instead of unboundedly spawning background work, replacing the
/// previous fire-and-forget <c>Task.Run</c> that had no concurrency limit.
/// </para>
/// </remarks>
public sealed class GraphIngestionChannel
{
    private readonly Channel<IngestionJob> _channel = Channel.CreateBounded<IngestionJob>(
        new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    /// <summary>Writer used by producers to enqueue graph ingestion jobs.</summary>
    public ChannelWriter<IngestionJob> Writer => _channel.Writer;

    /// <summary>Reader used by <see cref="GraphIngestionBackgroundService"/> to dequeue jobs.</summary>
    public ChannelReader<IngestionJob> Reader => _channel.Reader;
}
