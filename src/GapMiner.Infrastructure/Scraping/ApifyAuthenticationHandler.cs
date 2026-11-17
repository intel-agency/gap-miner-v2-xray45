using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// Delegating handler that injects the Apify API bearer token into every
/// request's <c>Authorization</c> header (plan T-2.1 / R5: the token comes from
/// <c>IConfiguration["Apify:Token"]</c> — Aspire secret parameter in
/// development, never a hardcoded constant). The token is read per send so a
/// configuration reload is honored; a missing or blank token fails fast with
/// <see cref="InvalidOperationException"/> instead of sending an unauthenticated
/// request the API would only reject with 401.
/// </summary>
public sealed class ApifyAuthenticationHandler : DelegatingHandler
{
    /// <summary>The configuration key holding the Apify API token.</summary>
    public const string TokenConfigurationKey = "Apify:Token";

    private readonly IConfiguration configuration;

    /// <summary>
    /// Initializes the handler with the configuration to read the token from.
    /// </summary>
    /// <param name="configuration">
    /// Application configuration carrying the token at
    /// <see cref="TokenConfigurationKey"/>.
    /// </param>
    public ApifyAuthenticationHandler(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this.configuration = configuration;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// No token is configured at <see cref="TokenConfigurationKey"/>.
    /// </exception>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = configuration[TokenConfigurationKey];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                $"The Apify API token is not configured. Set '{TokenConfigurationKey}' (Apify secret parameter) before calling the Apify client.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
