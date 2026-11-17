using GapMiner.Domain.Enums;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// The static registry (plan T-2.2) mapping each <see cref="MarketplaceKind"/>
/// to the Apify actor that scrapes its reviews and the actor's dataset-item
/// field names. Resolving a
/// <see cref="MarketplaceActorEntryStatus.NeedsVerification"/> entry throws
/// <see cref="MarketplaceNotSupportedException"/> so an unverified actor can
/// never silently produce garbage scrapes.
/// </summary>
/// <remarks>
/// <para>
/// Every entry in <see cref="Default"/> was verified against the live Apify
/// Store on 2026-11-17, per the plan's agent note ("verify actor IDs against
/// the live Apify Store; if uncertain, flag for human review"). All four
/// plan-candidate IDs (<c>apify/shopify-scraper</c>,
/// <c>apify/google-play-scraper</c>, <c>apify/apple-app-store-reviews-scraper</c>,
/// <c>apify/g2-scraper</c>) returned 404 on the store — each entry below is a
/// verified equivalent the plan's AC-1 sanctions for Shopify ("or verified
/// equivalent actor ID") and CWS ("adapt for CWS if an actor exists" — a
/// dedicated Chrome Web Store reviews actor exists, so the Google Play
/// fallback is unnecessary). Each entry's
/// <see cref="MarketplaceActorEntry.RequiredInputKeys"/> carries the actor's
/// required input keys for Story 3.3's job (canonical form; the per-entry
/// remarks document accepted alternatives), and its <c>VerificationNote</c>
/// cites the store page the field names were read from.
/// </para>
/// <para>
/// Verification is point-in-time: actors are community-maintured and their
/// output schemas can drift. If a scrape starts failing field mapping, mark
/// the entry <see cref="MarketplaceActorEntryStatus.NeedsVerification"/>
/// (resolving then throws) and re-verify against the store.
/// </para>
/// </remarks>
public sealed class MarketplaceActorRegistry
{
    /// <summary>
    /// The registry for apps.shopify.com: scrapes the listing's app reviews
    /// with automatic pagination. Required input: <c>appReviewUrl</c> (the
    /// listing's review page URL); optional <c>maxItems</c>.
    /// </summary>
    private static readonly MarketplaceActorEntry ShopifyAppStoreEntry = new(
        MarketplaceKind.ShopifyAppStore,
        "powerai~shopify-app-reviews-scraper",
        SourceReviewIdFieldName: "id",
        ReviewFieldName: "review",
        StarRatingFieldName: "rating",
        AuthorFieldName: "reviewer",
        DateFieldName: "date",
        RequiredInputKeys: new[] { "appReviewUrl" },
        MarketplaceActorEntryStatus.Verified,
        VerificationNote: "Live Apify Store verified 2026-11-17: apify.com/powerai/shopify-app-reviews-scraper — actor resolves (2.7k runs, 121 users); README documents the output fields incl. an example dataset item. Note: this actor emits 'date' in a human-readable format (e.g. \"October 21, 2025\"), not ISO 8601 — parsing belongs to Story 3.3's mapping.");

    /// <summary>
    /// The registry for chromewebstore.google.com: scrapes extension reviews
    /// with rating/date/language filtering applied before rows are saved.
    /// Required input: <c>startUrls</c> (extension detail or /reviews URLs) or
    /// <c>extensionIds</c>; the plan's stars 1–3 filter maps to
    /// <c>minRating</c>/<c>maxRating</c> (inclusive 1–5).
    /// </summary>
    private static readonly MarketplaceActorEntry ChromeWebStoreEntry = new(
        MarketplaceKind.ChromeWebStore,
        "automation-lab~chrome-web-store-reviews-scraper",
        SourceReviewIdFieldName: "reviewId",
        ReviewFieldName: "reviewBody",
        StarRatingFieldName: "rating",
        AuthorFieldName: "reviewerName",
        DateFieldName: "reviewDate",
        RequiredInputKeys: new[] { "startUrls" },
        MarketplaceActorEntryStatus.Verified,
        VerificationNote: "Live Apify Store verified 2026-11-17: apify.com/automation-lab/chrome-web-store-reviews-scraper — actor resolves (101 users, 98.5% success); README carries a field table and example output item (reviewDate ISO 8601); changelog active 2026-11-17.");

    /// <summary>
    /// The registry for apps.apple.com: scrapes app reviews by App Store ID or
    /// URL. Required input: <c>appIds</c> + <c>country</c> (Apple caps output
    /// at 500 reviews per country) or <c>startUrls</c>; optional
    /// <c>maxItems</c>.
    /// </summary>
    private static readonly MarketplaceActorEntry AppleAppStoreEntry = new(
        MarketplaceKind.AppleAppStore,
        "thewolves~appstore-reviews-scraper",
        SourceReviewIdFieldName: "id",
        ReviewFieldName: "text",
        StarRatingFieldName: "score",
        AuthorFieldName: "userName",
        DateFieldName: "date",
        RequiredInputKeys: new[] { "appIds", "country" },
        MarketplaceActorEntryStatus.Verified,
        VerificationNote: "Live Apify Store verified 2026-11-17: apify.com/thewolves/appstore-reviews-scraper — actor resolves (3.4k users, 4.98 stars, 246k runs); README documents a 12-field output reference incl. an example item (date ISO 8601, score 1-5).");

    /// <summary>
    /// The registry for g2.com: scrapes a product's reviews. Required input:
    /// <c>mode</c> = <c>product_reviews</c> and <c>productUrls</c>; optional
    /// <c>maxReviews</c>, <c>sortReviews</c>, <c>minRating</c>/<c>maxRating</c>
    /// (NPS 0–10, not stars) and <c>publishedAfter</c>. Review rows are also
    /// mirrored into the run's default dataset, which is the dataset
    /// <c>run-sync-get-dataset-items</c> returns.
    /// </summary>
    private static readonly MarketplaceActorEntry G2Entry = new(
        MarketplaceKind.G2,
        "automation-lab~g2-scraper",
        SourceReviewIdFieldName: "reviewId",
        ReviewFieldName: "reviewText",
        StarRatingFieldName: "starRating",
        AuthorFieldName: "reviewerName",
        DateFieldName: "publishedAt",
        RequiredInputKeys: new[] { "mode", "productUrls" },
        MarketplaceActorEntryStatus.Verified,
        VerificationNote: "Live Apify Store verified 2026-11-17: apify.com/automation-lab/g2-scraper — actor resolves (1.3k users, 5.0 stars, 100% 30-day success); README documents a 42-field review table incl. an example item (starRating 1-5 derived from NPS, publishedAt ISO 8601).");

    private static readonly IReadOnlyDictionary<MarketplaceKind, MarketplaceActorEntry> DefaultEntries =
        new Dictionary<MarketplaceKind, MarketplaceActorEntry>
        {
            [MarketplaceKind.ShopifyAppStore] = ShopifyAppStoreEntry,
            [MarketplaceKind.ChromeWebStore] = ChromeWebStoreEntry,
            [MarketplaceKind.AppleAppStore] = AppleAppStoreEntry,
            [MarketplaceKind.G2] = G2Entry,
        };

    /// <summary>
    /// The production registry: one live-store-verified entry per
    /// <see cref="MarketplaceKind"/>.
    /// </summary>
    public static MarketplaceActorRegistry Default { get; } = new(DefaultEntries);

    /// <summary>
    /// Initializes a registry from the given entries, validating that every
    /// entry is keyed by its own marketplace and carries a complete mapping.
    /// </summary>
    /// <param name="entries">The entries, keyed by marketplace.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entries"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="entries"/> is empty, an entry is keyed by a marketplace
    /// other than its own, an entry has a blank actor ID or field name, an
    /// entry has null, empty, or blank required input keys, or an entry has
    /// an undefined status.
    /// </exception>
    public MarketplaceActorRegistry(IReadOnlyDictionary<MarketplaceKind, MarketplaceActorEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new ArgumentException("The marketplace actor registry needs at least one entry.", nameof(entries));
        }

        foreach (var (kind, entry) in entries)
        {
            if (entry.MarketplaceKind != kind)
            {
                throw new ArgumentException(
                    $"Entry for {entry.MarketplaceKind} is keyed by {kind}; each entry must be keyed by its own marketplace.",
                    nameof(entries));
            }

            if (string.IsNullOrWhiteSpace(entry.ActorId))
            {
                throw new ArgumentException($"Entry for {kind} has a blank ActorId.", nameof(entries));
            }

            if (entry.RequiredInputKeys is null || entry.RequiredInputKeys.Count == 0)
            {
                throw new ArgumentException($"Entry for {kind} has no required input keys.", nameof(entries));
            }

            foreach (var inputKey in entry.RequiredInputKeys)
            {
                if (string.IsNullOrWhiteSpace(inputKey))
                {
                    throw new ArgumentException($"Entry for {kind} has a blank required input key.", nameof(entries));
                }
            }

            foreach (var (fieldName, value) in new[]
            {
                (nameof(MarketplaceActorEntry.SourceReviewIdFieldName), entry.SourceReviewIdFieldName),
                (nameof(MarketplaceActorEntry.ReviewFieldName), entry.ReviewFieldName),
                (nameof(MarketplaceActorEntry.StarRatingFieldName), entry.StarRatingFieldName),
                (nameof(MarketplaceActorEntry.AuthorFieldName), entry.AuthorFieldName),
                (nameof(MarketplaceActorEntry.DateFieldName), entry.DateFieldName),
            })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException($"Entry for {kind} has a blank {fieldName}.", nameof(entries));
                }
            }

            if (!Enum.IsDefined(entry.Status))
            {
                throw new ArgumentException($"Entry for {kind} has an undefined status {(int)entry.Status}.", nameof(entries));
            }
        }

        Entries = new Dictionary<MarketplaceKind, MarketplaceActorEntry>(entries);
    }

    /// <summary>Gets the registry's entries, keyed by marketplace.</summary>
    public IReadOnlyDictionary<MarketplaceKind, MarketplaceActorEntry> Entries { get; }

    /// <summary>
    /// Resolves the registry entry for a marketplace — the lookup Story 3.3's
    /// scrape job uses before triggering an actor run.
    /// </summary>
    /// <param name="marketplaceKind">The marketplace to scrape.</param>
    /// <returns>The verified entry carrying the actor ID and field mapping.</returns>
    /// <exception cref="MarketplaceNotSupportedException">
    /// The marketplace has no registry entry, or its entry is marked
    /// <see cref="MarketplaceActorEntryStatus.NeedsVerification"/> (actor not
    /// verified against the live Apify Store).
    /// </exception>
    public MarketplaceActorEntry Resolve(MarketplaceKind marketplaceKind)
    {
        if (!Entries.TryGetValue(marketplaceKind, out var entry))
        {
            throw new MarketplaceNotSupportedException(
                marketplaceKind,
                "the registry has no entry for this marketplace.");
        }

        if (entry.Status == MarketplaceActorEntryStatus.NeedsVerification)
        {
            throw new MarketplaceNotSupportedException(
                marketplaceKind,
                $"actor '{entry.ActorId}' is marked {nameof(MarketplaceActorEntryStatus.NeedsVerification)} " +
                "and must be verified against the live Apify Store before it can be scraped.");
        }

        return entry;
    }

    /// <summary>
    /// Looks up the registry entry for a marketplace without throwing — the
    /// introspection path for callers that enumerate or report on entries
    /// (including <see cref="MarketplaceActorEntryStatus.NeedsVerification"/>
    /// ones) rather than scraping.
    /// </summary>
    /// <remarks>
    /// The scrape path must use <see cref="Resolve"/>: this lookup
    /// deliberately skips the <c>NeedsVerification</c> guard, so a caller
    /// that scrapes through <see cref="TryGet"/> would bypass the
    /// fail-fast contract (review NIT, PR #52).
    /// </remarks>
    /// <param name="marketplaceKind">The marketplace to look up.</param>
    /// <param name="entry">
    /// The entry, whatever its status; <see langword="null"/> when the
    /// marketplace has no entry.
    /// </param>
    /// <returns><see langword="true"/> when an entry exists.</returns>
    public bool TryGet(MarketplaceKind marketplaceKind, out MarketplaceActorEntry? entry)
        => Entries.TryGetValue(marketplaceKind, out entry!);
}
