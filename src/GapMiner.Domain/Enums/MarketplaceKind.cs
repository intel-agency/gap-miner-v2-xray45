namespace GapMiner.Domain.Enums;

/// <summary>
/// The marketplace a competitor target is listed on.
/// </summary>
public enum MarketplaceKind
{
    /// <summary>apps.shopify.com.</summary>
    ShopifyAppStore = 0,

    /// <summary>chromewebstore.google.com.</summary>
    ChromeWebStore = 1,

    /// <summary>apps.apple.com.</summary>
    AppleAppStore = 2,

    /// <summary>g2.com.</summary>
    G2 = 3,
}
