using GapMiner.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using Xunit;

namespace GapMiner.Infrastructure.Tests.AI;

/// <summary>
/// Configuration-binding tests for the T-3.1 Semantic Kernel bootstrap: each
/// <c>AI:Provider</c> value must resolve the intended service registration —
/// no live key involved (fake in-memory values exercise registration only).
/// </summary>
public class SemanticKernelBootstrapTests
{
    private static IConfiguration BuildConfig(params (string Key, string? Value)[] pairs)
    {
        var source = pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value));
        return new ConfigurationBuilder().AddInMemoryCollection(source).Build();
    }

    private static IServiceCollection MinimalServices(IConfiguration configuration) =>
        new ServiceCollection().AddGapMinerSemanticKernel(configuration);

    [Fact]
    public void Azure_provider_registers_AzureOpenAI_chat_and_embedding_services()
    {
        var configuration = BuildConfig(
            ("AI:Provider", "Azure"),
            ("AI:Azure:Endpoint", "https://gapminer.openai.azure.com/"),
            ("AI:Azure:ApiKey", "test-azure-key"));

        var provider = MinimalServices(configuration).BuildServiceProvider();

        var chat = provider.GetRequiredService<IChatCompletionService>();
        Assert.IsType<AzureOpenAIChatCompletionService>(chat);

        var embeddings = provider.GetRequiredService<ITextEmbeddingGenerationService>();
        Assert.IsType<AzureOpenAITextEmbeddingGenerationService>(embeddings);

        // The Kernel is resolvable from the same provider and sees the same
        // registrations — Stories 4.2+ consume it.
        var kernel = provider.GetRequiredService<Kernel>();
        Assert.Same(chat, kernel.GetRequiredService<IChatCompletionService>());
        Assert.Same(embeddings, kernel.GetRequiredService<ITextEmbeddingGenerationService>());
    }

    [Fact]
    public void Anthropic_provider_registers_connector_chat_and_OpenAI_embeddings()
    {
        var configuration = BuildConfig(
            ("AI:Provider", "Anthropic"),
            ("AI:Anthropic:ApiKey", "test-anthropic-key"),
            ("AI:OpenAI:ApiKey", "test-openai-key"));

        var provider = MinimalServices(configuration).BuildServiceProvider();

        var chat = provider.GetRequiredService<IChatCompletionService>();
        var adapter = Assert.IsType<AnthropicChatCompletionService>(chat);
        Assert.Equal(SemanticKernelBuilderExtensions.DefaultAnthropicModel, adapter.Attributes["model"]);

        var embeddings = provider.GetRequiredService<ITextEmbeddingGenerationService>();
        Assert.IsType<OpenAITextEmbeddingGenerationService>(embeddings);
    }

    [Fact]
    public void Anthropic_provider_honors_model_and_endpoint_overrides()
    {
        var configuration = BuildConfig(
            ("AI:Provider", "Anthropic"),
            ("AI:Anthropic:ApiKey", "test-anthropic-key"),
            ("AI:Anthropic:Model", "claude-sonnet-4-5-20250929"),
            ("AI:Anthropic:Endpoint", "https://gw.example.com/anthropic"),
            ("AI:OpenAI:ApiKey", "test-openai-key"),
            ("AI:OpenAI:Endpoint", "https://gw.example.com/openai"),
            ("AI:OpenAI:EmbeddingModel", "text-embedding-3-large"));

        var provider = MinimalServices(configuration).BuildServiceProvider();

        var chat = Assert.IsType<AnthropicChatCompletionService>(provider.GetRequiredService<IChatCompletionService>());
        Assert.Equal("claude-sonnet-4-5-20250929", chat.Attributes["model"]);
        Assert.IsType<OpenAITextEmbeddingGenerationService>(provider.GetRequiredService<ITextEmbeddingGenerationService>());
    }

    [Fact]
    public void Provider_matching_is_case_insensitive()
    {
        var configuration = BuildConfig(
            ("AI:Provider", "azure"),
            ("AI:Azure:Endpoint", "https://gapminer.openai.azure.com/"),
            ("AI:Azure:ApiKey", "test-azure-key"));

        var provider = MinimalServices(configuration).BuildServiceProvider();
        Assert.IsType<AzureOpenAIChatCompletionService>(provider.GetRequiredService<IChatCompletionService>());
    }

    [Fact]
    public void Kernel_builder_overload_registers_the_same_services()
    {
        var configuration = BuildConfig(
            ("AI:Provider", "Anthropic"),
            ("AI:Anthropic:ApiKey", "test-anthropic-key"),
            ("AI:OpenAI:ApiKey", "test-openai-key"));

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddGapMinerSemanticKernel(configuration);
        var kernel = kernelBuilder.Build();

        Assert.IsType<AnthropicChatCompletionService>(kernel.GetRequiredService<IChatCompletionService>());
        Assert.IsType<OpenAITextEmbeddingGenerationService>(kernel.GetRequiredService<ITextEmbeddingGenerationService>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_provider_fails_fast(string? providerValue)
    {
        var configuration = BuildConfig(("AI:Provider", providerValue));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MinimalServices(configuration));

        Assert.Contains(SemanticKernelBuilderExtensions.ProviderConfigurationKey, ex.Message);
        Assert.Contains("environment variable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Claude")]
    [InlineData("azure-openai")]
    public void Unknown_provider_fails_fast(string providerValue)
    {
        var configuration = BuildConfig(("AI:Provider", providerValue));

        var ex = Assert.Throws<InvalidOperationException>(
            () => MinimalServices(configuration));

        Assert.Contains(providerValue, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Azure", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Anthropic", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AI:Azure:Endpoint", new[] { "AI:Provider", "AI:Azure:ApiKey" })]
    [InlineData("AI:Azure:ApiKey", new[] { "AI:Provider", "AI:Azure:Endpoint" })]
    [InlineData("AI:Anthropic:ApiKey", new[] { "AI:Provider", "AI:OpenAI:ApiKey" })]
    [InlineData("AI:OpenAI:ApiKey", new[] { "AI:Provider", "AI:Anthropic:ApiKey" })]
    public void Missing_branch_credentials_fail_fast(string missingKey, string[] presentKeys)
    {
        // Provider value follows the branch of the missing key.
        var branch = missingKey.StartsWith("AI:Azure", StringComparison.Ordinal) ? "Azure" : "Anthropic";
        var pairs = new List<(string, string?)> { ("AI:Provider", branch) };
        foreach (var key in presentKeys.Where(k => k != "AI:Provider"))
        {
            pairs.Add((key, key.EndsWith("Endpoint", StringComparison.Ordinal) ? "https://gapminer.example.com/" : "some-key"));
        }

        var configuration = BuildConfig(pairs.ToArray());

        var ex = Assert.Throws<InvalidOperationException>(
            () => MinimalServices(configuration));

        Assert.Contains(missingKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains("appsettings.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_arguments_fail_fast()
    {
        IConfiguration configuration = BuildConfig(("AI:Provider", "Azure"));
        IServiceCollection? nullServices = null;

        Assert.Throws<ArgumentNullException>(() => nullServices!.AddGapMinerSemanticKernel(configuration));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddGapMinerSemanticKernel(null!));
        Assert.Throws<ArgumentNullException>(() => Kernel.CreateBuilder().AddGapMinerSemanticKernel(null!));
    }
}
