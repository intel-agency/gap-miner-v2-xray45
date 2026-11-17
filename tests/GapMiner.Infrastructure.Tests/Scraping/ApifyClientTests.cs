using System.Net;
using System.Text;
using GapMiner.Infrastructure.Scraping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Refit;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Scraping;

/// <summary>
/// Story 3.1 acceptance for the Apify v2 Refit client: a mocked
/// <see cref="HttpMessageHandler"/> behind the real DI registration
/// (<c>AddApifyClient</c>) validates each endpoint's request shape — method,
/// path template, query parameters, bearer header and JSON body — plus the
/// typed response DTOs, the Polly retry behavior (429/5xx retried, 408 not
/// retried, exactly 3 retries, exponential backoff shrunk for test speed),
/// the sync-run-aware client timeout and the fail-fast missing-token guard.
/// Wire formats follow the Apify API v2 reference the implementation was
/// verified against.
/// </summary>
public sealed class ApifyClientTests
{
    private const string Token = "apify_unit_test_token_5ZQtM3";

    /// <summary>A run object shaped like the docs' "Get Actor run" example.</summary>
    private const string RunJson =
        """
        {"data":{"id":"HG7ML7M8z78YcAPEB","actId":"HDSasDasz78YcAPEB","startedAt":"2019-11-30T07:34:24.202Z",
        "finishedAt":"2019-12-12T09:30:12.202Z","status":"SUCCEEDED","statusMessage":"Actor finished",
        "isStatusMessageTerminal":true,"options":{"build":"latest","timeoutSecs":300,"memoryMbytes":1024,
        "diskMbytes":2048},"buildId":"7sT5jcggjjA9fNcxF","buildNumber":"0.0.36","exitCode":0,
        "defaultKeyValueStoreId":"eJNzqsbPiopwJcgGQ","defaultDatasetId":"wmKPijuyDnPZAPRMk",
        "defaultRequestQueueId":"FL35cSF7jrxr3BY39"}}
        """;

    /// <summary>Dataset items shaped like a marketplace review scrape.</summary>
    private const string ItemsJson =
        """
        [{"url":"https://apps.shopify.com/rank-tracker-pro/reviews/1042","rating":2,
        "text":"Sync kept failing for weeks","reviewedAt":"2026-10-04T00:00:00.000Z"},
        {"url":"https://apps.shopify.com/rank-tracker-pro/reviews/1043","rating":1,
        "text":"Support never replied","reviewedAt":"2026-10-05T00:00:00.000Z"}]
        """;

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// Builds the client through the real DI registration with the recording
    /// handler as the primary <see cref="HttpMessageHandler"/> and a
    /// milliseconds-scale retry delay.
    /// </summary>
    private static (IApifyClient Client, RecordingHttpMessageHandler Handler) CreateClient(
        RecordingHttpMessageHandler handler,
        string? token = Token,
        Action<HttpRetryStrategyOptions>? configureRetry = null)
    {
        var settings = new Dictionary<string, string?>();
        if (token is not null)
        {
            settings[ApifyAuthenticationHandler.TokenConfigurationKey] = token;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddApifyClient(
            configuration,
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(_ => handler),
            configureRetry: configureRetry ?? (options =>
            {
                options.Delay = TimeSpan.FromMilliseconds(1);
                options.UseJitter = false;
            }));

        var client = services.BuildServiceProvider().GetRequiredService<IApifyClient>();
        return (client, handler);
    }

    [Fact]
    public async Task RunSyncGetDatasetItems_posts_input_bearer_and_query_to_the_run_sync_endpoint()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.Created, ItemsJson));
        var (client, recorder) = CreateClient(handler);

        var items = await client.RunSyncGetDatasetItemsAsync(
            "apify~web-scraper",
            new { startUrl = "https://apps.shopify.com/rank-tracker-pro", maxReviews = 50 },
            timeout: 300,
            maxItems: 50);

        var request = Assert.Single(recorder.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "/v2/actors/apify~web-scraper/run-sync-get-dataset-items?timeout=300&maxItems=50",
            request.RequestUri!.PathAndQuery);
        Assert.Equal($"Bearer {Token}", request.Headers.Authorization?.ToString());
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"startUrl":"https://apps.shopify.com/rank-tracker-pro","maxReviews":50}""",
            Assert.Single(recorder.RequestBodies));

        Assert.Equal(2, items.Count);
        Assert.Equal(2, items[0].Fields!["rating"].GetInt32());
        Assert.Equal("Sync kept failing for weeks", items[0].Fields!["text"].GetString());
    }

    [Fact]
    public async Task RunSyncGetDatasetItems_omits_unset_query_parameters()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.Created, "[]"));
        var (client, recorder) = CreateClient(handler);

        await client.RunSyncGetDatasetItemsAsync("apify~web-scraper", new { startUrl = "https://example.com" });

        var request = Assert.Single(recorder.Requests);
        Assert.Equal("/v2/actors/apify~web-scraper/run-sync-get-dataset-items", request.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task RunActor_posts_input_bearer_and_query_to_the_run_start_endpoint()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.Created, RunJson));
        var (client, recorder) = CreateClient(handler);

        var response = await client.RunActorAsync(
            "apify~web-scraper",
            new Dictionary<string, object?>
            {
                ["startUrls"] = new[] { new Dictionary<string, string> { ["url"] = "https://example.com" } },
                ["minRating"] = 1,
                ["maxRating"] = 3,
            },
            timeout: 300,
            maxItems: 50);

        var request = Assert.Single(recorder.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "/v2/actors/apify~web-scraper/runs?timeout=300&maxItems=50",
            request.RequestUri!.PathAndQuery);
        Assert.Equal($"Bearer {Token}", request.Headers.Authorization?.ToString());
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"startUrls":[{"url":"https://example.com"}],"minRating":1,"maxRating":3}""",
            Assert.Single(recorder.RequestBodies));

        // The 201 response is the { data: { run } } envelope the poll loop
        // seeds from: run id, status, and the dataset id the download step uses.
        var run = Assert.IsType<ApifyActorRun>(response.Data);
        Assert.Equal("HG7ML7M8z78YcAPEB", run.Id);
        Assert.Equal(ApifyRunStatus.Succeeded, run.Status);
        Assert.Equal("wmKPijuyDnPZAPRMk", run.DefaultDatasetId);
    }

    [Fact]
    public async Task RunActor_omits_unset_query_parameters()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.Created, RunJson));
        var (client, recorder) = CreateClient(handler);

        await client.RunActorAsync("apify~web-scraper", new { startUrl = "https://example.com" });

        var request = Assert.Single(recorder.Requests);
        Assert.Equal("/v2/actors/apify~web-scraper/runs", request.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task GetActorRun_gets_the_bearer_enveloped_run_object()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.OK, RunJson));
        var (client, recorder) = CreateClient(handler);

        var response = await client.GetActorRunAsync("HG7ML7M8z78YcAPEB");

        var request = Assert.Single(recorder.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v2/actor-runs/HG7ML7M8z78YcAPEB", request.RequestUri!.PathAndQuery);
        Assert.Equal($"Bearer {Token}", request.Headers.Authorization?.ToString());

        var run = Assert.IsType<ApifyActorRun>(response.Data);
        Assert.Equal("HG7ML7M8z78YcAPEB", run.Id);
        Assert.Equal("HDSasDasz78YcAPEB", run.ActId);
        Assert.Equal(ApifyRunStatus.Succeeded, run.Status);
        Assert.Equal("Actor finished", run.StatusMessage);
        Assert.True(run.IsStatusMessageTerminal);
        Assert.Equal(2019, run.StartedAt!.Value.Year);
        Assert.Equal(2019, run.FinishedAt!.Value.Year);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("wmKPijuyDnPZAPRMk", run.DefaultDatasetId);
        Assert.Equal("eJNzqsbPiopwJcgGQ", run.DefaultKeyValueStoreId);
        Assert.Equal("latest", run.Options!.Build);
        Assert.Equal(300, run.Options.TimeoutSecs);
        Assert.Equal(1024, run.Options.MemoryMbytes);
    }

    [Fact]
    public async Task GetActorRun_passes_waitForFinish_as_query_parameter()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.OK, RunJson));
        var (client, recorder) = CreateClient(handler);

        await client.GetActorRunAsync("HG7ML7M8z78YcAPEB", waitForFinish: 30);

        Assert.Equal("/v2/actor-runs/HG7ML7M8z78YcAPEB?waitForFinish=30", Assert.Single(recorder.Requests).RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task GetDatasetItems_gets_bearer_and_query_parameters_lowercase_booleans()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.OK, ItemsJson));
        var (client, recorder) = CreateClient(handler);

        var items = await client.GetDatasetItemsAsync("wmKPijuyDnPZAPRMk", clean: true, offset: 0, limit: 100);

        var request = Assert.Single(recorder.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/v2/datasets/wmKPijuyDnPZAPRMk/items?clean=true&offset=0&limit=100",
            request.RequestUri!.PathAndQuery);
        Assert.Equal($"Bearer {Token}", request.Headers.Authorization?.ToString());
        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[1].Fields!["rating"].GetInt32());
    }

    [Fact]
    public async Task GetDatasetItems_encodes_the_fields_list_query_parameter()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.OK, ItemsJson));
        var (client, recorder) = CreateClient(handler);

        await client.GetDatasetItemsAsync("wmKPijuyDnPZAPRMk", fields: "url,rating");

        var query = Assert.Single(recorder.Requests).RequestUri!.Query;
        Assert.Contains("fields=", query, StringComparison.Ordinal);
        Assert.DoesNotContain("clean=", query, StringComparison.Ordinal);
        Assert.DoesNotContain("offset=", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_policy_retries_a_429_response_then_succeeds()
    {
        var handler = new RecordingHttpMessageHandler(
            () => Json((HttpStatusCode)429, "{}"),
            () => Json(HttpStatusCode.Created, ItemsJson));
        var (client, recorder) = CreateClient(handler);

        var items = await client.RunSyncGetDatasetItemsAsync("apify~web-scraper", new { startUrl = "https://example.com" });

        Assert.Equal(2, items.Count);
        Assert.Equal(2, recorder.Requests.Count);
        Assert.All(recorder.Requests, request =>
            Assert.Equal($"Bearer {Token}", request.Headers.Authorization?.ToString()));
    }

    [Fact]
    public async Task Retry_policy_retries_a_5xx_response_then_succeeds()
    {
        var handler = new RecordingHttpMessageHandler(
            () => Json(HttpStatusCode.InternalServerError, "{}"),
            () => Json(HttpStatusCode.OK, RunJson));
        var (client, recorder) = CreateClient(handler);

        var response = await client.GetActorRunAsync("HG7ML7M8z78YcAPEB");

        Assert.NotNull(response.Data);
        Assert.Equal(2, recorder.Requests.Count);
    }

    [Fact]
    public async Task Retry_policy_does_not_retry_a_408_sync_run_timeout()
    {
        // Apify documents 408 on run-sync-get-dataset-items as "the run
        // exceeded the 300 s limit" with the run NOT aborted — retrying
        // would start a duplicate (paid) actor run, so 408 must surface.
        // https://docs.apify.com/api/v2/actor-run-sync-get-dataset-items
        var handler = new RecordingHttpMessageHandler(
            () => Json(HttpStatusCode.RequestTimeout, "{}"),
            () => Json(HttpStatusCode.Created, ItemsJson));
        var (client, recorder) = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.RunSyncGetDatasetItemsAsync("apify~web-scraper", new { startUrl = "https://example.com" }));

        Assert.Equal(HttpStatusCode.RequestTimeout, exception.StatusCode);
        Assert.Single(recorder.Requests);
    }

    [Fact]
    public async Task Retry_policy_gives_up_after_exactly_three_retries()
    {
        var handler = new RecordingHttpMessageHandler(() => Json((HttpStatusCode)429, "{}"));
        var (client, recorder) = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetDatasetItemsAsync("wmKPijuyDnPZAPRMk"));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(4, recorder.Requests.Count);
    }

    [Fact]
    public void Client_timeout_covers_the_documented_300_second_sync_run_wait()
    {
        // The run-sync endpoint holds the connection for the whole run
        // (documented max wait 300 s); the framework's 100 s default would
        // abort it client-side, so AddApifyClient must raise it.
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.OK, "[]"));
        TimeSpan? observedTimeout = null;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [ApifyAuthenticationHandler.TokenConfigurationKey] = Token }).Build();
        var services = new ServiceCollection();
        services.AddApifyClient(
            configuration,
            configureClient: builder => builder.ConfigureHttpClient(client => observedTimeout = client.Timeout),
            configureRetry: options => options.Delay = TimeSpan.FromMilliseconds(1));

        _ = services.BuildServiceProvider().GetRequiredService<IApifyClient>();

        Assert.Equal(ApifyClientServiceCollectionExtensions.ClientTimeout, observedTimeout);
        Assert.True(ApifyClientServiceCollectionExtensions.ClientTimeout > TimeSpan.FromSeconds(300));
    }

    [Fact]
    public async Task Missing_token_fails_fast_without_sending_a_request()
    {
        var handler = new RecordingHttpMessageHandler(() => Json(HttpStatusCode.OK, "[]"));
        var (client, recorder) = CreateClient(handler, token: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetDatasetItemsAsync("wmKPijuyDnPZAPRMk"));

        Assert.Empty(recorder.Requests);
    }

    [Fact]
    public void AddApifyClient_rejects_null_arguments()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [ApifyAuthenticationHandler.TokenConfigurationKey] = Token }).Build();

        Assert.Throws<ArgumentNullException>(() => ApifyClientServiceCollectionExtensions.AddApifyClient(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddApifyClient(null!));
    }
}
