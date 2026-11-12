using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using Xunit;

namespace GapMiner.Domain.Tests;

public class CompetitorTargetTests
{
    [Fact]
    public void Constructor_AssignsAllProperties()
    {
        var id = Guid.NewGuid();
        var url = new Uri("https://apps.shopify.com/competitor-app");
        var createdAt = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

        var target = new CompetitorTarget(
            id: id,
            name: "Competitor App",
            marketplaceKind: MarketplaceKind.ShopifyAppStore,
            marketplaceUrl: url,
            createdAt: createdAt);

        Assert.Equal(id, target.Id);
        Assert.Equal("Competitor App", target.Name);
        Assert.Equal(MarketplaceKind.ShopifyAppStore, target.MarketplaceKind);
        Assert.Same(url, target.MarketplaceUrl);
        Assert.Equal(createdAt, target.CreatedAt);
    }
}
