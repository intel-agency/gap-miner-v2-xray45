using Refit;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// Typed Refit client for the four Apify API v2 endpoints the scraper pipeline
/// uses (plan T-2.1 + the T-2.3 poll loop): asynchronous actor triggering,
/// synchronous actor triggering, actor-run status polling, and dataset-item
/// retrieval. Register through
/// <see cref="ApifyClientServiceCollectionExtensions.AddApifyClient"/> so the
/// bearer delegating handler and the Polly retry policy are attached — the
/// endpoints are not authenticated or resilient when implemented by hand.
/// </summary>
/// <remarks>
/// Response shapes verified against the Apify API v2 reference. Note that
/// <c>run-sync-get-dataset-items</c> returns the run's dataset items directly
/// (HTTP 201, bare JSON array) — not a run object; run metadata comes from
/// <see cref="GetActorRunAsync"/>. The run endpoints use the documented
/// <c>/v2/actors/</c> prefix — the only form the live OpenAPI spec
/// (docs.apify.com/api/openapi.json, checked 2026-11-17) lists. The legacy
/// <c>/v2/acts/</c> spelling still routes today (probed live: an auth-gated
/// 402, not a 404; Apify's own store READMEs still print it) but appears
/// nowhere in the spec, so new code must not depend on it.
/// </remarks>
public interface IApifyClient
{
    /// <summary>
    /// Triggers an actor run and returns immediately, without waiting for it to
    /// finish (<c>POST /v2/actors/{actorId}/runs</c>) — the run-start endpoint of
    /// the scrape pipeline's poll loop, whose 60-minute budget exceeds what the
    /// synchronous endpoints can hold (they cap at 300 seconds and answer 408).
    /// The request body is the actor input JSON; the response (HTTP 201) is the
    /// <c>{ "data": { run } }</c> envelope with the run's ID and status, from
    /// which <see cref="GetActorRunAsync"/> polls until a terminal status and
    /// <see cref="ApifyActorRun.DefaultDatasetId"/> feeds the dataset download.
    /// </summary>
    /// <param name="actorId">
    /// Actor ID or <c>username~actor-name</c> (e.g. <c>apify~web-scraper</c>).
    /// Resolved from the Story 3.2 marketplace registry.
    /// </param>
    /// <param name="actorInput">
    /// The actor input object, serialized as camelCase JSON per the actor's
    /// input schema (target URL, review filter, and so on).
    /// </param>
    /// <param name="timeout">Optional run timeout in seconds.</param>
    /// <param name="memory">Optional memory limit for the run, in megabytes.</param>
    /// <param name="maxItems">Optional cap on charged dataset items.</param>
    /// <param name="build">Optional actor build tag or number to run.</param>
    /// <param name="cancellationToken">Propagates cancellation (R4).</param>
    /// <returns>The enveloped run — <see cref="ApifyActorRun.Id"/> polls,
    /// <see cref="ApifyActorRun.Status"/> starts as READY or RUNNING.</returns>
    [Post("/v2/actors/{actorId}/runs")]
    Task<ApifyRunResponse> RunActorAsync(
        string actorId,
        [Body] object actorInput,
        int? timeout = null,
        int? memory = null,
        int? maxItems = null,
        string? build = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers an actor run, waits for it synchronously, and returns the
    /// run's dataset items (<c>POST /v2/actors/{actorId}/run-sync-get-dataset-items</c>).
    /// The request body is the actor input JSON; the response body is the
    /// dataset items as a bare JSON array (HTTP 201). If the run exceeds 300
    /// seconds the endpoint responds 408.
    /// </summary>
    /// <param name="actorId">
    /// Actor ID or <c>username~actor-name</c> (e.g. <c>apify~web-scraper</c>).
    /// Resolved from the Story 3.2 marketplace registry.
    /// </param>
    /// <param name="actorInput">
    /// The actor input object, serialized as camelCase JSON per the actor's
    /// input schema (target URL, review filter, and so on).
    /// </param>
    /// <param name="timeout">Optional run timeout in seconds.</param>
    /// <param name="memory">Optional memory limit for the run, in megabytes.</param>
    /// <param name="maxItems">Optional cap on charged dataset items.</param>
    /// <param name="build">Optional actor build tag or number to run.</param>
    /// <param name="cancellationToken">Propagates cancellation (R4).</param>
    /// <returns>The run's dataset items, oldest first.</returns>
    [Post("/v2/actors/{actorId}/run-sync-get-dataset-items")]
    Task<IReadOnlyList<ApifyDatasetItem>> RunSyncGetDatasetItemsAsync(
        string actorId,
        [Body] object actorInput,
        int? timeout = null,
        int? memory = null,
        int? maxItems = null,
        string? build = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an actor run's status and metadata
    /// (<c>GET /v2/actor-runs/{runId}</c>) — the polling endpoint of the
    /// scrape pipeline. The response is a <c>{ "data": { run } }</c> envelope.
    /// </summary>
    /// <param name="runId">The actor run ID.</param>
    /// <param name="waitForFinish">
    /// Optional: seconds the server waits for a terminal status (max 60)
    /// before answering with the current transitional status.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation (R4).</param>
    /// <returns>The enveloped run; <see cref="ApifyRunResponse.Data"/> carries
    /// the typed run with <see cref="ApifyActorRun.Status"/> and
    /// <see cref="ApifyActorRun.DefaultDatasetId"/>.</returns>
    [Get("/v2/actor-runs/{runId}")]
    Task<ApifyRunResponse> GetActorRunAsync(
        string runId,
        double? waitForFinish = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a dataset's items as JSON
    /// (<c>GET /v2/datasets/{datasetId}/items</c>) — a bare JSON array in the
    /// endpoint's default <c>json</c> format. Use
    /// <see cref="ApifyActorRun.DefaultDatasetId"/> from the run status as the
    /// dataset ID.
    /// </summary>
    /// <param name="datasetId">The dataset ID (or <c>username~dataset-name</c>).</param>
    /// <param name="clean">
    /// Optional: when <see langword="true"/>, skips empty items and hidden
    /// fields (those starting with <c>#</c>).
    /// </param>
    /// <param name="offset">Optional: number of items to skip at the start.</param>
    /// <param name="limit">Optional: maximum number of items to return.</param>
    /// <param name="fields">
    /// Optional: comma-separated list of fields to pick from each item.
    /// </param>
    /// <param name="cancellationToken">Propagates cancellation (R4).</param>
    /// <returns>The dataset items, in storage order (oldest first).</returns>
    [Get("/v2/datasets/{datasetId}/items")]
    Task<IReadOnlyList<ApifyDatasetItem>> GetDatasetItemsAsync(
        string datasetId,
        bool? clean = null,
        long? offset = null,
        long? limit = null,
        string? fields = null,
        CancellationToken cancellationToken = default);
}
