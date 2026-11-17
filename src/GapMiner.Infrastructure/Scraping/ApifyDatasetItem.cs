using System.Text.Json;
using System.Text.Json.Serialization;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// A single item of an Apify dataset. Dataset items are actor-defined free-form
/// JSON objects — each marketplace actor emits its own field names (mapped per
/// marketplace in Story 3.2) — so the typed envelope carries the item's fields
/// as <see cref="System.Text.Json.JsonElement"/> extension data rather than
/// inventing fixed properties. Verified against the Apify API v2 reference
/// ("Get dataset items"): the response is a bare JSON array of item objects,
/// not wrapped in a data envelope.
/// </summary>
public sealed class ApifyDatasetItem
{
    /// <summary>
    /// All top-level fields of the item, keyed by the actor's field name.
    /// <see langword="null"/> when the item is an empty object.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Fields { get; set; }
}
