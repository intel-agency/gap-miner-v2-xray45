using System.Runtime.CompilerServices;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AnthropicTextContent = Anthropic.SDK.Messaging.TextContent;

namespace GapMiner.Infrastructure.AI;

/// <summary>
/// Semantic Kernel <see cref="IChatCompletionService"/> for Anthropic Claude,
/// adapting the community <c>Anthropic.SDK</c> client to the kernel's service
/// abstraction (plan T-3.1, "Anthropic Claude via community connector").
/// </summary>
/// <remarks>
/// <para>
/// No NuGet package ships such an adapter compatible with the plan's
/// Microsoft.SemanticKernel 1.20.0 pin (live-verified 2026-11-21):
/// <c>Anthropic.SDK</c> itself integrates with the Microsoft.Extensions.AI
/// <c>IChatClient</c> abstraction only, and the community
/// <c>JD.SemanticKernel.Connectors.ClaudeCode</c> package requires
/// SK >= 1.82.0. This adapter therefore wraps the community SDK client —
/// the wire protocol, auth, retries and SSE parsing stay in
/// <c>Anthropic.SDK</c>; only the thin SK service contract lives here.
/// </para>
/// <para>
/// Token usage is surfaced on every non-streaming result via the
/// <see cref="InputTokensMetadataKey"/>/<see cref="OutputTokensMetadataKey"/>
/// metadata entries — the durable hook Story 7.2 consumes for the
/// <c>gapminer.llm.tokens.consumed</c> metric.
/// </para>
/// </remarks>
public sealed class AnthropicChatCompletionService : IChatCompletionService
{
    /// <summary>Metadata key carrying the input (prompt) token count of a completion.</summary>
    public const string InputTokensMetadataKey = "Usage.InputTokens";

    /// <summary>Metadata key carrying the output (completion) token count of a completion.</summary>
    public const string OutputTokensMetadataKey = "Usage.OutputTokens";

    /// <summary>
    /// Max output tokens sent when the caller's execution settings carry no
    /// explicit value. Anthropic requires <c>max_tokens</c> on every request.
    /// </summary>
    public const int DefaultMaxTokens = 1024;

    private readonly AnthropicClient _client;
    private readonly string _model;

    /// <summary>Initializes the adapter. Attributes expose the model id to callers/diagnostics.</summary>
    /// <param name="apiKey">Anthropic API key (from environment or Aspire secrets — R5).</param>
    /// <param name="model">Anthropic model id, e.g. <c>claude-3-5-sonnet-20241022</c>.</param>
    /// <param name="endpoint">
    /// Optional Anthropic-wire-compatible endpoint override (gateway/proxy).
    /// Requests go to <c>{endpoint}/v1/messages</c>; the SDK default
    /// (<c>https://api.anthropic.com</c>) applies when null/blank.
    /// </param>
    /// <param name="httpClient">
    /// Optional HTTP client — the unit tests substitute a stubbed handler to
    /// exercise the request/response mapping without a live key.
    /// </param>
    public AnthropicChatCompletionService(string apiKey, string model, string? endpoint = null, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Anthropic API key is required (environment variable or Aspire secret — R5).", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Anthropic model id is required.", nameof(model));
        }

        _client = new AnthropicClient(new APIAuthentication(apiKey), httpClient ?? new HttpClient());
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            // The SDK builds URLs from this format string ({0} = "v1",
            // {1} = "messages"); it does not read HttpClient.BaseAddress.
            _client.ApiUrlFormat = endpoint.TrimEnd('/') + "/{0}/{1}";
        }

        _model = model.Trim();
        Attributes = new Dictionary<string, object?> { ["model"] = _model };
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Attributes { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings,
        Kernel? kernel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);

        var parameters = ToMessageParameters(chatHistory, executionSettings, stream: false);
        var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken).ConfigureAwait(false);

        // Extended-thinking models prepend thinking blocks; only text blocks
        // carry the assistant message the pipeline consumes.
        var text = string.Concat(response.Content.OfType<AnthropicTextContent>().Select(block => block.Text));

        var metadata = new Dictionary<string, object?>
        {
            ["Anthropic.MessageId"] = response.Id,
            ["Anthropic.Model"] = response.Model,
            ["Anthropic.StopReason"] = response.StopReason,
            [InputTokensMetadataKey] = response.Usage?.InputTokens ?? 0,
            [OutputTokensMetadataKey] = response.Usage?.OutputTokens ?? 0,
        };

        return new[] { new ChatMessageContent(AuthorRole.Assistant, text) { Metadata = metadata } };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings,
        Kernel? kernel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);

        var parameters = ToMessageParameters(chatHistory, executionSettings, stream: true);
        var usage = new Dictionary<string, object?>();

        await foreach (var frame in _client.Messages.StreamClaudeMessageAsync(parameters, cancellationToken).ConfigureAwait(false))
        {
            if (frame.Delta?.Text is { Length: > 0 } deltaText)
            {
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, deltaText) { Metadata = usage };
            }

            // The final message_delta frame carries the run's cumulative
            // token usage — on the wire (and in the SDK's frame model) it is
            // a sibling of the delta payload — so attach it to the shared
            // metadata dictionary and consumers reading the accumulated
            // stream can find the counts.
            var frameUsage = frame.Usage ?? frame.Delta?.Usage;
            if (frameUsage is not null)
            {
                usage[InputTokensMetadataKey] = frameUsage.InputTokens;
                usage[OutputTokensMetadataKey] = frameUsage.OutputTokens;
            }
        }
    }

    private MessageParameters ToMessageParameters(ChatHistory chatHistory, PromptExecutionSettings? executionSettings, bool stream)
    {
        var messages = new List<Message>();
        var systemMessages = new List<SystemMessage>();

        foreach (var message in chatHistory)
        {
            var text = message.Content?.ToString() ?? string.Empty;
            if (message.Role == AuthorRole.System)
            {
                systemMessages.Add(new SystemMessage(text));
            }
            else
            {
                // Anthropic accepts only user/assistant turns; tool and
                // function roles have no Story 4.x producer yet and map to
                // the user bucket like any unknown role.
                var role = message.Role == AuthorRole.Assistant ? RoleType.Assistant : RoleType.User;
                messages.Add(new Message(role, text));
            }
        }

        if (messages.Count == 0)
        {
            throw new ArgumentException("Chat history must contain at least one user or assistant message.", nameof(chatHistory));
        }

        var maxTokens = DefaultMaxTokens;
        decimal? temperature = null;
        if (executionSettings?.ExtensionData is { Count: > 0 } extensions)
        {
            if (extensions.TryGetValue("max_tokens", out var maxObj) && maxObj is int configuredMax)
            {
                maxTokens = configuredMax;
            }

            if (extensions.TryGetValue("temperature", out var tempObj) && tempObj is double configuredTemp)
            {
                temperature = (decimal)configuredTemp;
            }
        }

        return new MessageParameters
        {
            Model = _model,
            Messages = messages,
            System = systemMessages.Count > 0 ? systemMessages : null,
            MaxTokens = maxTokens,
            Temperature = temperature,
            Stream = stream,
        };
    }
}
