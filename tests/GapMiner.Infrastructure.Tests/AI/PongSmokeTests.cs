using System.Collections;
using GapMiner.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;
using Xunit.Abstractions;

namespace GapMiner.Infrastructure.Tests.AI;

/// <summary>
/// T-3.1 acceptance smoke test: a single prompt <c>"Return the word PONG"</c>
/// through the REAL bootstrap registration returns <c>PONG</c>, with the
/// response's <c>Usage</c> token counts logged (the hook Story 7.2 turns into
/// the <c>gapminer.llm.tokens.consumed</c> metric).
/// </summary>
/// <remarks>
/// The test resolves the provider selected by the <c>AI__Provider</c>
/// environment variable (R5: keys are sourced exclusively from environment
/// variables — never appsettings.json). With no AI configuration present
/// (e.g. CI) it soft-skips with an explanatory line; locally, export the
/// AI__* variables for the desired branch, e.g.:
/// <code>
/// AI__Provider=Anthropic
/// AI__Anthropic__ApiKey=&lt;key&gt;
/// AI__Anthropic__Model=&lt;model&gt;
/// AI__Anthropic__Endpoint=&lt;optional Anthropic-wire gateway&gt;
/// AI__OpenAI__ApiKey=&lt;placeholder — chat-only smoke&gt;
/// </code>
/// </remarks>
public class PongSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Prompt_returns_PONG_with_usage_token_counts_logged()
    {
        // Configuration from environment variables ONLY (R5): every AI__*
        // variable maps to its colon-separated configuration key.
        var values = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string key && key.StartsWith("AI__", StringComparison.Ordinal))
            .Select(entry => new KeyValuePair<string, string?>(
                ((string)entry.Key).Replace("__", ":", StringComparison.Ordinal),
                (string?)entry.Value));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var provider = configuration[SemanticKernelBuilderExtensions.ProviderConfigurationKey];
        if (string.IsNullOrWhiteSpace(provider))
        {
            output.WriteLine("SMOKE SKIPPED: AI:Provider is not configured. Export the AI__* environment variables for the desired branch and re-run.");
            return;
        }

        var services = new ServiceCollection().AddGapMinerSemanticKernel(configuration);
        await using var serviceProvider = services.BuildServiceProvider();
        var chat = serviceProvider.GetRequiredService<IChatCompletionService>();
        output.WriteLine($"SMOKE: provider={provider} chatService={chat.GetType().Name}");

        var history = new ChatHistory();
        history.AddUserMessage("Return the word PONG");

        var results = await chat.GetChatMessageContentsAsync(history, null, null, CancellationToken.None);
        var result = Assert.Single(results);

        // Usage token counts logged first so the counts are visible even if
        // the content assertion below fails.
        if (result.Metadata is not null)
        {
            foreach (var (key, value) in result.Metadata)
            {
                output.WriteLine($"SMOKE metadata: {key}={value}");
            }
        }
        else
        {
            output.WriteLine("SMOKE metadata: (none returned by this provider's connector)");
        }

        Assert.Equal("PONG", result.Content);

        // The adapter-backed branch guarantees usage metadata; other branches
        // log whatever their connector returned above.
        if (chat is AnthropicChatCompletionService)
        {
            Assert.NotNull(result.Metadata);
            var input = Assert.IsType<int>(result.Metadata[AnthropicChatCompletionService.InputTokensMetadataKey]);
            var completion = Assert.IsType<int>(result.Metadata[AnthropicChatCompletionService.OutputTokensMetadataKey]);
            Assert.True(input > 0 || completion > 0, $"usage token counts must be positive (input={input}, output={completion})");
            output.WriteLine($"SMOKE RESULT: PONG received; usage inputTokens={input} outputTokens={completion}");
        }
    }
}
