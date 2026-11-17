using GapMiner.Domain.Enums;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// One marketplace's mapping in the static actor registry (plan T-2.2): which
/// Apify actor scrapes its reviews, and which names that actor's dataset
/// items use for the fields the pipeline persists. Field names are the actor's
/// own output keys — each marketplace actor emits a different schema, which is
/// why <see cref="ApifyDatasetItem"/> carries items as free-form JSON.
/// </summary>
/// <param name="MarketplaceKind">The marketplace the entry maps.</param>
/// <param name="ActorId">
/// The actor ID in the Apify API's <c>username~actor-name</c> form (e.g.
/// <c>apify~web-scraper</c>) — the single-segment form the
/// <c>/v2/actors/{actorId}/…</c> endpoints require; the store-URL form with a
/// slash would break path routing.
/// </param>
/// <param name="SourceReviewIdFieldName">
/// The dataset item field holding the marketplace's external review ID. Feeds
/// <c>Review.SourceReviewId</c>, the idempotency key of the persistence layer
/// (Story 2.2's unique composite), so re-scrapes never duplicate rows.
/// Documented for every verified actor even though plan T-2.2's field list
/// stops at the four below — without it the T-2.3 mapping could not construct
/// a <c>Review</c> at all.
/// </param>
/// <param name="ReviewFieldName">The dataset item field holding the review body text.</param>
/// <param name="StarRatingFieldName">
/// The dataset item field holding the star rating (1–5 —
/// <c>Review.StarRating</c>'s validated range).
/// </param>
/// <param name="AuthorFieldName">The dataset item field holding the reviewer's display name.</param>
/// <param name="DateFieldName">
/// The dataset item field holding the review's publication date.
/// </param>
/// <param name="RequiredInputKeys">
/// The actor input keys a run for this marketplace must set — the pipeline's
/// canonical input form, read from the actor's live store-page input schema.
/// Where an actor accepts alternative forms (e.g. Chrome Web Store URLs or
/// raw extension IDs), the registry entry's remarks document them; Story
/// 3.3's job builds its run input from these keys.
/// </param>
/// <param name="Status">
/// Whether the entry was verified against the live Apify Store. A
/// <see cref="MarketplaceActorEntryStatus.NeedsVerification"/> entry throws
/// <see cref="MarketplaceNotSupportedException"/> when resolved — never
/// silently fails.
/// </param>
/// <param name="VerificationNote">
/// How and when the entry was verified (store-page URL and what it showed);
/// <see langword="null"/> for unverified entries.
/// </param>
public sealed record MarketplaceActorEntry(
    MarketplaceKind MarketplaceKind,
    string ActorId,
    string SourceReviewIdFieldName,
    string ReviewFieldName,
    string StarRatingFieldName,
    string AuthorFieldName,
    string DateFieldName,
    IReadOnlyList<string> RequiredInputKeys,
    MarketplaceActorEntryStatus Status,
    string? VerificationNote = null);
