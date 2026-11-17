using System.Text.Json.Serialization;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// Typed view of an Apify actor run as returned by the run-status endpoints
/// (shape verified against the Apify API v2 reference, "Get Actor run" — the
/// body is a <c>{ "data": { … } }</c> envelope parsed by
/// <see cref="ApifyRunResponse"/>). A deliberately minimal subset of the
/// documented fields: the scraper pipeline needs identity, status, timing and
/// the default storage IDs (the dataset ID is what feeds
/// <c>GET /v2/datasets/{datasetId}/items</c>), not billing or usage stats.
/// Fields absent here are ignored on deserialization.
/// </summary>
public sealed class ApifyActorRun
{
    /// <summary>Unique identifier of the run.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>ID of the actor that was run.</summary>
    [JsonPropertyName("actId")]
    public string? ActId { get; set; }

    /// <summary>ID of the actor task, when the run was started from a task.</summary>
    [JsonPropertyName("actorTaskId")]
    public string? ActorTaskId { get; set; }

    /// <summary>Time the run started.</summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Time the run finished; <see langword="null"/> while it is still running.</summary>
    [JsonPropertyName("finishedAt")]
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// The run status, one of the documented <see cref="ApifyRunStatus"/>
    /// values (compared as raw strings so undocumented new statuses still
    /// deserialize).
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Detailed message about the run status.</summary>
    [JsonPropertyName("statusMessage")]
    public string? StatusMessage { get; set; }

    /// <summary>Whether <see cref="StatusMessage"/> is terminal (final).</summary>
    [JsonPropertyName("isStatusMessageTerminal")]
    public bool? IsStatusMessageTerminal { get; set; }

    /// <summary>The configuration the run was started with.</summary>
    [JsonPropertyName("options")]
    public ApifyRunOptions? Options { get; set; }

    /// <summary>ID of the actor build used for this run.</summary>
    [JsonPropertyName("buildId")]
    public string? BuildId { get; set; }

    /// <summary>Build number of the actor build used for this run.</summary>
    [JsonPropertyName("buildNumber")]
    public string? BuildNumber { get; set; }

    /// <summary>Exit code of the run process; <see langword="null"/> while running.</summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    /// <summary>ID of the run's default key-value store.</summary>
    [JsonPropertyName("defaultKeyValueStoreId")]
    public string? DefaultKeyValueStoreId { get; set; }

    /// <summary>ID of the run's default dataset — the input for dataset-item retrieval.</summary>
    [JsonPropertyName("defaultDatasetId")]
    public string? DefaultDatasetId { get; set; }

    /// <summary>ID of the run's default request queue.</summary>
    [JsonPropertyName("defaultRequestQueueId")]
    public string? DefaultRequestQueueId { get; set; }
}
