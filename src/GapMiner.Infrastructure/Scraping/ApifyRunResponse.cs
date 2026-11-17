using System.Text.Json.Serialization;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// Envelope Apify API v2 wraps single-object responses in
/// (<c>{ "data": { … } }</c>), specialized for the actor-run object returned
/// by <c>GET /v2/actor-runs/{runId}</c>. Array responses (dataset items) are
/// not enveloped and deserialize directly into item lists.
/// </summary>
public sealed class ApifyRunResponse
{
    /// <summary>The wrapped actor run.</summary>
    [JsonPropertyName("data")]
    public ApifyActorRun? Data { get; set; }
}
