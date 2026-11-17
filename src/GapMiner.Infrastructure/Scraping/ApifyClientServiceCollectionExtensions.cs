using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Refit;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// Registers the typed Apify v2 client (plan T-2.1) with its bearer
/// delegating handler and Polly retry policy. The retry is the Polly v8
/// pipeline shipped with <c>Microsoft.Extensions.Http.Resilience</c> (already
/// the solution's pinned resilience package from Story 1.3): 3 retries on
/// 429/5xx with exponential backoff, per the story's acceptance criteria.
/// </summary>
public static class ApifyClientServiceCollectionExtensions
{
    /// <summary>The Apify API v2 base address.</summary>
    public const string BaseAddress = "https://api.apify.com";

    /// <summary>Name of the client's Polly retry pipeline (telemetry-friendly).</summary>
    public const string RetryPipelineName = "apify-retry";

    /// <summary>Retry count mandated by the story: 3 retries after the initial attempt.</summary>
    public const int RetryCount = 3;

    /// <summary>
    /// The base delay of the exponential backoff (attempt <c>n</c> waits
    /// roughly <c>2^n</c> times this, plus jitter).
    /// </summary>
    public static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Registers <see cref="IApifyClient"/> as a typed Refit client against
    /// <see cref="BaseAddress"/>, with
    /// <see cref="ApifyAuthenticationHandler"/> injecting the bearer token
    /// from <c>IConfiguration["Apify:Token"]</c> and a Polly retry pipeline
    /// (3 retries, exponential backoff) around transient 429/5xx responses.
    /// </summary>
    /// <param name="services">The collection to register the client with.</param>
    /// <param name="configuration">
    /// Application configuration carrying the API token at
    /// <see cref="ApifyAuthenticationHandler.TokenConfigurationKey"/>.
    /// </param>
    /// <param name="configureClient">
    /// Optional extra configuration of the underlying
    /// <see cref="Microsoft.Extensions.DependencyInjection.IHttpClientBuilder"/>
    /// — the unit tests use it to substitute the primary
    /// <see cref="HttpMessageHandler"/> with a recording fake.
    /// </param>
    /// <param name="configureRetry">
    /// Optional overrides of the retry strategy (the unit tests shrink the
    /// backoff delay); <see cref="RetryCount"/>, exponential backoff and the
    /// default 429/5xx transient classification stay in force unless changed
    /// here.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddApifyClient(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IHttpClientBuilder>? configureClient = null,
        Action<HttpRetryStrategyOptions>? configureRetry = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var retryOptions = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = RetryCount,
            BackoffType = DelayBackoffType.Exponential,
            Delay = RetryBaseDelay,
            UseJitter = true,
        };
        configureRetry?.Invoke(retryOptions);

        // Bound to the configuration instance passed in (per-send token reads honor
        // config reloads and fail fast on a missing token) without registering an
        // app-wide IConfiguration service.
        services.TryAddTransient<ApifyAuthenticationHandler>(_ => new ApifyAuthenticationHandler(configuration));

        var clientBuilder = services
            .AddRefitClient<IApifyClient>(new RefitSettings
            {
                UrlParameterFormatter = new ApifyUrlParameterFormatter(),
            })
            .ConfigureHttpClient(client => client.BaseAddress = new Uri(BaseAddress))
            .AddHttpMessageHandler<ApifyAuthenticationHandler>();

        clientBuilder.AddResilienceHandler(RetryPipelineName, pipeline => pipeline.AddRetry(retryOptions));

        configureClient?.Invoke(clientBuilder);

        return services;
    }
}
