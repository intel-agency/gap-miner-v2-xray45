using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using GapMiner.Domain.Entities;
using GapMiner.Infrastructure.Scraping;

namespace GapMiner.ScraperWorker.Jobs;

/// <summary>
/// The result of mapping one scrape's dataset items through a registry entry:
/// the <see cref="Reviews"/> that could be mapped losslessly, plus how many
/// items were <see cref="SkippedItemCount"/> skipped (missing fields,
/// out-of-range ratings, unparseable dates) so the job can log the loss
/// instead of silently dropping rows.
/// </summary>
/// <param name="Reviews">The mapped reviews, in dataset order.</param>
/// <param name="SkippedItemCount">The dataset items that produced no review.</param>
public sealed record DatasetMappingResult(IReadOnlyList<Review> Reviews, int SkippedItemCount);

/// <summary>
/// Maps Apify dataset items to <see cref="Review"/> entities using the
/// marketplace registry's field names (plan T-2.3, lifecycle step 6) — each
/// marketplace actor emits its own schema, so the mapper reads the entry's
/// field names (source review id, review body, star rating, author, date)
/// from every item's free-form JSON.
/// </summary>
/// <remarks>
/// <para>
/// Mapping is deliberately lossless-or-skip: an item whose id, review text,
/// star rating or date is missing or malformed is skipped and counted, never
/// guessed — a guessed value would poison the analysis pipeline the reviews
/// feed. Skipped items are the actor schema-drift signal the registry's
/// verification note warns about.
/// </para>
/// <para>
/// Dates parse through <see cref="DateTimeOffset.TryParse(string, out DateTimeOffset)"/>
/// with <see cref="DateTimeStyles.AssumeUniversal"/> |
/// <see cref="DateTimeStyles.AdjustToUniversal"/>: ISO 8601 timestamps (Chrome
/// Web Store, Apple, G2) and the Shopify actor's human-readable dates
/// (e.g. <c>"October 21, 2025"</c>, an invariant calendar pattern) both parse,
/// and timestamps without an offset normalize to UTC rather than picking up
/// the host's local zone.
/// </para>
/// </remarks>
public sealed class DatasetReviewMapper
{
    /// <summary>
    /// Maps dataset items to reviews through the registry entry's field names.
    /// The star-rating field accepts whole numbers 1–5 (the
    /// <see cref="Review"/> constructor's validated range) either as JSON
    /// numbers or numeric strings; a missing author field maps to an empty
    /// name (the review body — the analysis input — is what matters, and the
    /// author is not an idempotency key).
    /// </summary>
    /// <param name="entry">The registry entry carrying the actor's field names.</param>
    /// <param name="items">The run's dataset items.</param>
    /// <param name="targetId">The competitor target the reviews belong to.</param>
    /// <returns>The mapped reviews and the count of skipped items.</returns>
    public DatasetMappingResult Map(
        MarketplaceActorEntry entry,
        IReadOnlyList<ApifyDatasetItem> items,
        Guid targetId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(items);

        var reviews = new List<Review>(items.Count);
        var skipped = 0;

        foreach (var item in items)
        {
            if (TryMapItem(entry, item, targetId, out var review))
            {
                reviews.Add(review);
            }
            else
            {
                skipped++;
            }
        }

        return new DatasetMappingResult(reviews, skipped);
    }

    private static bool TryMapItem(
        MarketplaceActorEntry entry,
        ApifyDatasetItem item,
        Guid targetId,
        [NotNullWhen(true)] out Review? review)
    {
        review = null;
        var fields = item.Fields;
        if (fields is null)
        {
            return false;
        }

        if (!TryReadString(fields, entry.SourceReviewIdFieldName, out var sourceReviewId)
            || !TryReadString(fields, entry.ReviewFieldName, out var reviewText)
            || string.IsNullOrWhiteSpace(reviewText)
            || !TryReadRating(fields, entry.StarRatingFieldName, out var starRating)
            || !TryReadDate(fields, entry.DateFieldName, out var datePosted))
        {
            return false;
        }

        // A missing or non-string author is cosmetic, not a mapping failure.
        var author = fields.TryGetValue(entry.AuthorFieldName, out var authorElement)
            && authorElement.ValueKind == JsonValueKind.String
            ? authorElement.GetString()
            : null;

        review = new Review(
            id: Guid.NewGuid(),
            competitorTargetId: targetId,
            starRating: starRating,
            reviewText: reviewText,
            reviewAuthor: author ?? string.Empty,
            datePosted: datePosted,
            sourceReviewId: sourceReviewId);
        return true;
    }

    /// <summary>
    /// Reads a required string field. Numeric values (some actors emit ids as
    /// JSON numbers) map to their raw literal text so large ids survive
    /// precision-intact.
    /// </summary>
    private static bool TryReadString(
        Dictionary<string, JsonElement> fields,
        string fieldName,
        out string value)
    {
        value = string.Empty;
        if (!fields.TryGetValue(fieldName, out var element))
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                value = text;
                return true;
            case JsonValueKind.Number:
                value = element.GetRawText();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads the star rating: a whole number 1–5, as a JSON number or a
    /// numeric string. Fractions and out-of-range values skip the item — the
    /// <see cref="Review"/> constructor's range is a domain invariant, and a
    /// value outside it means the field mapping no longer matches the actor's
    /// output.
    /// </summary>
    private static bool TryReadRating(
        Dictionary<string, JsonElement> fields,
        string fieldName,
        out int rating)
    {
        rating = 0;
        if (!fields.TryGetValue(fieldName, out var element))
        {
            return false;
        }

        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out var number) ? (int?)number : null,
            JsonValueKind.String when int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromText)
                => fromText,
            _ => null,
        };

        if (parsed is not int value || value is < 1 or > 5)
        {
            return false;
        }

        rating = value;
        return true;
    }

    private static bool TryReadDate(
        Dictionary<string, JsonElement> fields,
        string fieldName,
        out DateTimeOffset datePosted)
    {
        datePosted = default;
        return fields.TryGetValue(fieldName, out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out datePosted);
    }
}
