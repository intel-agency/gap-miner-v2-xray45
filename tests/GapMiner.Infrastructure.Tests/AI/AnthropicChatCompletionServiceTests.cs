using System.Net;
using System.Text;
using System.Text.Json;
using GapMiner.Infrastructure.AI;
using GapMiner.Infrastructure.Tests.Scraping;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace GapMiner.Infrastructure.Tests.AI;

/// <summary>
/// Request/response mapping tests for the Anthropic community-connector
/// adapter, driven through a stubbed HTTP handler — no live key needed. The
/// canned payloads mirror the Anthropic Messages wire shapes the SDK
/// consumes (live-verified against an Anthropic-compatible endpoint on
/// 2026-11-21).
/// </summary>
public class AnthropicChatCompletionServiceTests
{
    private const string ResponseJson = """
        {
          "id": "msg_01TestMessageId",
          "type": "message",
          "role": "assistant",
          "model": "claude-3-5-sonnet-20241022",
          "content": [
            { "type": "thinking", "thinking": "internal reasoning", "signature": "sig" },
            { "type": "text", "text": "PONG" }
          ],
          "stop_reason": "end_turn",
          "stop_sequence": null,
          "usage": { "input_tokens": 17, "output_tokens": 27 }
        }
        """;

    private static readonly string StreamBody = string.Join(
        "\n\n",
        """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_01TestMessageId","type":"message","role":"assistant","model":"claude-3-5-sonnet-20241022","content":[],"usage":{"input_tokens":17,"output_tokens":1}}}
        """,
        """
        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}
        """,
        """
        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"PONG"}}
        """,
        """
        event: content_block_stop
        data: {"type":"content_block_stop","index":0}
        """,
        """
        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":4}}
        """,
        """
        event: message_stop
        data: {"type":"message_stop"}
        """) + "\n\n";

    private static HttpResponseMessage Json() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage EventStream() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(StreamBody, Encoding.UTF8, "text/event-stream"),
    };

    private static AnthropicChatCompletionService Service(
        RecordingHttpMessageHandler handler, string? endpoint = null) =>
        new("test-key", SemanticKernelBuilderExtensions.DefaultAnthropicModel, endpoint, new HttpClient(handler));

    [Fact]
    public async Task Completion_maps_text_blocks_and_usage_metadata()
    {
        var handler = new RecordingHttpMessageHandler(() => Json());
        var service = Service(handler);

        var history = new ChatHistory();
        history.AddUserMessage("Return the word PONG");

        var results = await service.GetChatMessageContentsAsync(history, null, null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(AuthorRole.Assistant, result.Role);
        Assert.Equal("PONG", result.Content);

        // Thinking blocks must not leak into the completion text.
        Assert.DoesNotContain("internal reasoning", result.Content, StringComparison.Ordinal);

        Assert.NotNull(result.Metadata);
        Assert.Equal(17, result.Metadata[AnthropicChatCompletionService.InputTokensMetadataKey]);
        Assert.Equal(27, result.Metadata[AnthropicChatCompletionService.OutputTokensMetadataKey]);
        Assert.Equal("msg_01TestMessageId", result.Metadata["Anthropic.MessageId"]);
        Assert.Equal("claude-3-5-sonnet-20241022", result.Metadata["Anthropic.Model"]);
        Assert.Equal("end_turn", result.Metadata["Anthropic.StopReason"]);
    }

    [Fact]
    public async Task Completion_request_maps_system_turns_settings_and_endpoint()
    {
        var handler = new RecordingHttpMessageHandler(() => Json());
        var service = Service(handler, endpoint: "https://gw.example.com/anthropic/");

        var history = new ChatHistory("You are the smoke-test system prompt.");
        history.AddUserMessage("Return the word PONG");
        history.AddAssistantMessage("PONG");

        var settings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["max_tokens"] = 99,
                ["temperature"] = 0.2,
            },
        };

        await service.GetChatMessageContentsAsync(history, settings, null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://gw.example.com/anthropic/v1/messages", request.RequestUri?.ToString());

        var recordedBody = handler.RequestBodies[0];
        Assert.NotNull(recordedBody);
        var body = JsonDocument.Parse(recordedBody);
        Assert.Equal(SemanticKernelBuilderExtensions.DefaultAnthropicModel, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(99, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.2, body.RootElement.GetProperty("temperature").GetDouble(), precision: 3);

        var system = body.RootElement.GetProperty("system");
        Assert.Equal("You are the smoke-test system prompt.", system[0].GetProperty("text").GetString());

        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("Return the word PONG", messages[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Default_endpoint_targets_the_Anthropic_API()
    {
        var handler = new RecordingHttpMessageHandler(() => Json());
        var service = Service(handler, endpoint: null);

        var history = new ChatHistory();
        history.AddUserMessage("Return the word PONG");

        await service.GetChatMessageContentsAsync(history, null, null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri?.ToString());
    }

    [Fact]
    public async Task Streaming_maps_text_deltas_and_final_usage()
    {
        var handler = new RecordingHttpMessageHandler(() => EventStream());
        var service = Service(handler);

        var history = new ChatHistory();
        history.AddUserMessage("Return the word PONG");

        var streamed = new List<StreamingChatMessageContent>();
        await foreach (var content in service.GetStreamingChatMessageContentsAsync(history, null, null, CancellationToken.None))
        {
            streamed.Add(content);
        }

        var text = string.Concat(streamed.Select(c => c.Content));
        Assert.Equal("PONG", text);
        Assert.All(streamed, c => Assert.Equal(AuthorRole.Assistant, c.Role));

        // The trailing message_delta frame fills the usage dictionary shared
        // with the streamed contents (Story 7.2's metric hook).
        var last = streamed[^1];
        Assert.NotNull(last.Metadata);
        Assert.Equal(4, last.Metadata[AnthropicChatCompletionService.OutputTokensMetadataKey]);
    }

    [Fact]
    public async Task History_without_user_or_assistant_turns_fails_fast()
    {
        var handler = new RecordingHttpMessageHandler(() => Json());
        var service = Service(handler);

        var history = new ChatHistory("system-only prompt");

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetChatMessageContentsAsync(history, null, null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Null_history_fails_fast()
    {
        var handler = new RecordingHttpMessageHandler(() => Json());
        var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.GetChatMessageContentsAsync(null!, null, null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_api_key_or_model_rejected_at_construction(string? blank)
    {
        Assert.Throws<ArgumentException>(() => new AnthropicChatCompletionService(blank!, "model"));
        Assert.Throws<ArgumentException>(() => new AnthropicChatCompletionService("key", blank!));
    }
}
