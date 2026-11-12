using System.Reflection;
using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using Xunit;

namespace GapMiner.Domain.Tests;

public class ImmutabilityTests
{
    // Derived from the assembly so entities added later are guarded automatically.
    public static TheoryData<Type> EntityTypes => new(
        typeof(CompetitorTarget).Assembly
            .GetExportedTypes()
            .Where(t => t.IsClass && t.Namespace == "GapMiner.Domain.Entities"));

    [Theory]
    [MemberData(nameof(EntityTypes))]
    public void EveryPublicProperty_IsGetOnlyOrInitOnly(Type entityType)
    {
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // nonPublic: true so a private/internal setter is still found and must
            // prove init-only (nonPublic: false conflates it with get-only).
            var setter = property.GetSetMethod(nonPublic: true);

            if (setter is null)
            {
                // Get-only: fully immutable — satisfies the AC.
                continue;
            }

            var isInitOnly = setter.ReturnParameter
                .GetRequiredCustomModifiers()
                .Any(modifier => modifier.Name == "IsExternalInit");

            Assert.True(
                isInitOnly,
                $"{entityType.Name}.{property.Name} must have an init-only setter.");
        }
    }

    [Fact]
    public void MarketplaceKind_CoversExactlyTheFourPlannedMarketplaces()
    {
        var expected = new[]
        {
            "ShopifyAppStore",
            "ChromeWebStore",
            "AppleAppStore",
            "G2",
        };

        Assert.Equal(expected, Enum.GetNames<MarketplaceKind>());
    }
}
