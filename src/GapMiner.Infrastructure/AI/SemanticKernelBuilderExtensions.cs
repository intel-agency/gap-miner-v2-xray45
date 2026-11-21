using System.ClientModel;
using Anthropic.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace GapMiner.Infrastructure.AI;

/// <summary>
/// Semantic Kernel bootstrap for the analysis pipeline (plan T-3.1). Registers
/// the chat-completion and embedding-generation services the GapMiner jobs run
/// on, switching the chat provider from configuration —
/// <see cref="ProviderConfigurationKey"/> selects <c>Azure</c> (Azure OpenAI
/// GPT-4o) or <c>Anthropic</c> (Claude via the community
/// <c>Anthropic.SDK</c> connector) with no code changes.
/// </summary>
/// <remarks>
/// <para>
/// Provider keys are read exclusively from the <see cref="IConfiguration"/>
/// passed in — i.e. from environment variables (the double-underscore form,
/// e.g. <c>AI__Azure__ApiKey</c>) or Aspire secret parameters, which surface
/// the same way. Keys are never written to <c>appsettings.json</c> (plan R5);
/// every required value that is missing or blank fails fast at registration
/// time.
/// </para>
/// <para>
/// Embeddings always resolve to <c>text-embedding-3-large</c> (3072
/// dimensions) — the only OpenAI-family model whose dimension matches the
/// <c>Review.Embedding</c> <c>vector(3072)</c> column (Story 2.2). On the
/// Azure branch both services share the Azure OpenAI endpoint/key; on the
/// Anthropic branch chat goes through <see cref="AnthropicChatCompletionService"/>
/// while embeddings use the OpenAI API, because Anthropic offers no embeddings
/// endpoint.
/// </para>
/// <para>
/// No NuGet package ships a Semantic-Kernel <c>IChatCompletionService</c>
/// connector for Anthropic that is compatible with the plan's SK 1.20.0 pin
/// (live-verified 2026-11-21: <c>Anthropic.SDK</c> integrates via
/// Microsoft.Extensions.AI <c>IChatClient</c> only; the
/// <c>JD.SemanticKernel.Connectors.ClaudeCode</c> connector requires
/// SK >= 1.82.0), so the Anthropic branch registers the in-repo
/// <see cref="AnthropicChatCompletionService"/> adapter over the community
/// <c>Anthropic.SDK</c> client.
/// </para>
/// </remarks>
public static class SemanticKernelBuilderExtensions
{
    /// <summary>Configuration key selecting the chat provider: <c>Azure</c> or <c>Anthropic</c> (case-insensitive).</summary>
    public const string ProviderConfigurationKey = "AI:Provider";

    /// <summary>Configuration key of the Azure OpenAI resource endpoint (required on the Azure branch).</summary>
    public const string AzureEndpointConfigurationKey = "AI:Azure:Endpoint";

    /// <summary>Configuration key of the Azure OpenAI API key (required on the Azure branch).</summary>
    public const string AzureApiKeyConfigurationKey = "AI:Azure:ApiKey";

    /// <summary>Configuration key overriding the Azure chat deployment name (defaults to <see cref="DefaultAzureChatDeployment"/>).</summary>
    public const string AzureChatDeploymentConfigurationKey = "AI:Azure:ChatDeployment";

    /// <summary>Configuration key overriding the Azure embedding deployment name (defaults to <see cref="DefaultEmbeddingModel"/>).</summary>
    public const string AzureEmbeddingDeploymentConfigurationKey = "AI:Azure:EmbeddingDeployment";

    /// <summary>Configuration key of the Anthropic API key (required on the Anthropic branch).</summary>
    public const string AnthropicApiKeyConfigurationKey = "AI:Anthropic:ApiKey";

    /// <summary>Configuration key overriding the Anthropic model id (defaults to <see cref="DefaultAnthropicModel"/>).</summary>
    public const string AnthropicModelConfigurationKey = "AI:Anthropic:Model";

    /// <summary>
    /// Configuration key overriding the Anthropic API endpoint (defaults to
    /// <see cref="DefaultAnthropicEndpoint"/>). Anthropic-wire-compatible
    /// gateways can be pointed at here; the path is always
    /// <c>{endpoint}/v1/messages</c>.
    /// </summary>
    public const string AnthropicEndpointConfigurationKey = "AI:Anthropic:Endpoint";

    /// <summary>Configuration key of the OpenAI API key used for embeddings on the Anthropic branch.</summary>
    public const string OpenAiApiKeyConfigurationKey = "AI:OpenAI:ApiKey";

    /// <summary>Configuration key overriding the OpenAI-compatible embeddings endpoint (defaults to the OpenAI API).</summary>
    public const string OpenAiEndpointConfigurationKey = "AI:OpenAI:Endpoint";

    /// <summary>Configuration key overriding the embeddings model (defaults to <see cref="DefaultEmbeddingModel"/>).</summary>
    public const string OpenAiEmbeddingModelConfigurationKey = "AI:OpenAI:EmbeddingModel";

    /// <summary>Provider value selecting Azure OpenAI for chat completions.</summary>
    public const string AzureProvider = "Azure";

    /// <summary>Provider value selecting Anthropic Claude for chat completions.</summary>
    public const string AnthropicProvider = "Anthropic";

    /// <summary>
    /// Default Azure chat deployment. The plan names GPT-4o as the analysis
    /// chat model; deployment names are per-resource in Azure OpenAI, so the
    /// <see cref="AzureChatDeploymentConfigurationKey"/> override exists for
    /// resources that named the deployment differently.
    /// </summary>
    public const string DefaultAzureChatDeployment = "gpt-4o";

    /// <summary>
    /// Default embeddings model. <c>text-embedding-3-large</c> emits 3072-dim
    /// vectors — the exact width of the <c>Review.Embedding</c> column
    /// (Story 2.2); a dimension mismatch would break Story 4.2's embed path
    /// and the <c>22000</c> constraint tests.
    /// </summary>
    public const string DefaultEmbeddingModel = "text-embedding-3-large";

    /// <summary>Default Anthropic chat model (Claude 3.5 Sonnet, per the plan's provider list).</summary>
    public const string DefaultAnthropicModel = "claude-3-5-sonnet-20241022";

    /// <summary>Default Anthropic API endpoint.</summary>
    public const string DefaultAnthropicEndpoint = "https://api.anthropic.com";

    /// <summary>
    /// Registers the Semantic Kernel chat-completion and embedding-generation
    /// services for the configured <see cref="ProviderConfigurationKey"/>
    /// provider, plus a resolvable <see cref="Kernel"/> over the same
    /// collection (Stories 4.2–4.4 consume it).
    /// </summary>
    /// <param name="services">The collection to register the services with.</param>
    /// <param name="configuration">
    /// Application configuration carrying the provider selection and keys —
    /// sourced from environment variables or Aspire secret parameters, never
    /// <c>appsettings.json</c> (R5).
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The provider is missing/unknown, or a required key/endpoint for the
    /// selected provider is missing or blank.
    /// </exception>
    public static IServiceCollection AddGapMinerSemanticKernel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration[ProviderConfigurationKey]?.Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                $"Missing configuration '{ProviderConfigurationKey}': set it to '{AzureProvider}' or '{AnthropicProvider}' "
                + "via an environment variable (AI__Provider) or Aspire parameter — never appsettings.json (R5).");
        }

        if (string.Equals(provider, AzureProvider, StringComparison.OrdinalIgnoreCase))
        {
            RegisterAzureProvider(services, configuration);
        }
        else if (string.Equals(provider, AnthropicProvider, StringComparison.OrdinalIgnoreCase))
        {
            RegisterAnthropicProvider(services, configuration);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown AI provider '{provider}' in '{ProviderConfigurationKey}': expected '{AzureProvider}' or '{AnthropicProvider}'.");
        }

        // Kernel over the same provider, so DI consumers and kernel consumers
        // see one consistent service set. TryAdd keeps any host-side Kernel
        // registration authoritative.
        services.TryAddSingleton<Kernel>(sp => new Kernel(sp, new KernelPluginCollection()));

        return services;
    }

    /// <summary>
    /// Kernel-builder flavor of <see cref="AddGapMinerSemanticKernel(IServiceCollection, IConfiguration)"/>
    /// for hosts that compose their kernel through <see cref="IKernelBuilder"/>
    /// (e.g. <c>Kernel.CreateBuilder()</c>) instead of a raw
    /// <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="kernelBuilder">The kernel builder whose service collection receives the registrations.</param>
    /// <param name="configuration">See the service-collection overload.</param>
    /// <returns>The same kernel builder, for chaining.</returns>
    public static IKernelBuilder AddGapMinerSemanticKernel(
        this IKernelBuilder kernelBuilder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(kernelBuilder);
        kernelBuilder.Services.AddGapMinerSemanticKernel(configuration);
        return kernelBuilder;
    }

    private static void RegisterAzureProvider(IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = Require(configuration, AzureEndpointConfigurationKey);
        var apiKey = Require(configuration, AzureApiKeyConfigurationKey);
        var chatDeployment = WithDefault(configuration[AzureChatDeploymentConfigurationKey], DefaultAzureChatDeployment);
        var embeddingDeployment = WithDefault(configuration[AzureEmbeddingDeploymentConfigurationKey], DefaultEmbeddingModel);

        // Both services ride the same Azure OpenAI resource and key.
        services.AddAzureOpenAIChatCompletion(chatDeployment, endpoint, apiKey);
        services.AddAzureOpenAITextEmbeddingGeneration(embeddingDeployment, endpoint, apiKey);
    }

    private static void RegisterAnthropicProvider(IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = Require(configuration, AnthropicApiKeyConfigurationKey);
        var model = WithDefault(configuration[AnthropicModelConfigurationKey], DefaultAnthropicModel);
        var endpoint = configuration[AnthropicEndpointConfigurationKey];

        // Chat: community Anthropic.SDK connector behind the in-repo
        // IChatCompletionService adapter (see the class remarks for why no
        // package connector exists at the plan's SK 1.20.0 pin).
        services.AddSingleton<IChatCompletionService>(
            _ => new AnthropicChatCompletionService(apiKey, model, endpoint));

        // Embeddings: Anthropic has no embeddings API, so the Anthropic branch
        // generates vectors through the OpenAI API (text-embedding-3-large,
        // 3072-dim — the Review.Embedding column width). An optional
        // OpenAI-compatible endpoint override covers gateways/proxies.
        var openAiApiKey = Require(configuration, OpenAiApiKeyConfigurationKey);
        var embeddingModel = WithDefault(configuration[OpenAiEmbeddingModelConfigurationKey], DefaultEmbeddingModel);
        var openAiEndpoint = configuration[OpenAiEndpointConfigurationKey];
        if (string.IsNullOrWhiteSpace(openAiEndpoint))
        {
            services.AddOpenAITextEmbeddingGeneration(embeddingModel, openAiApiKey);
        }
        else
        {
            var openAiClient = new OpenAI.OpenAIClient(
                new ApiKeyCredential(openAiApiKey),
                new OpenAI.OpenAIClientOptions { Endpoint = new Uri(openAiEndpoint.TrimEnd('/')) });
            services.AddOpenAITextEmbeddingGeneration(embeddingModel, openAiClient);
        }
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing required configuration '{key}'. Source AI provider keys from environment variables "
                + "(double-underscore form, e.g. AI__Anthropic__ApiKey) or Aspire secret parameters — never appsettings.json (R5).");
        }

        return value.Trim();
    }

    private static string WithDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
