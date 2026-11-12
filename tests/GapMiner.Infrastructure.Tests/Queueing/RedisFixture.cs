using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Queueing;

/// <summary>
/// One shared Redis container for the queueing integration tests — the same
/// redis:7.4 image:tag the AppHost pins. xunit runs every class in the
/// <see cref="RedisCollection"/> sequentially against it; tests use distinct
/// per-test queue keys so they cannot observe each other's messages.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("redis:7.4").Build();

    private IConnectionMultiplexer? multiplexer;

    /// <summary>
    /// The shared connection multiplexer to the container's Redis server.
    /// </summary>
    public IConnectionMultiplexer Redis =>
        multiplexer ?? throw new InvalidOperationException("The fixture is not initialized.");

    async Task IAsyncLifetime.InitializeAsync()
    {
        await container.StartAsync();
        multiplexer = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (multiplexer is not null)
        {
            await multiplexer.DisposeAsync();
        }

        await container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis integration";
}
