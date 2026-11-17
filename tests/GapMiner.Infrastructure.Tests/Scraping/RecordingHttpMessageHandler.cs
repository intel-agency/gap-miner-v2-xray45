namespace GapMiner.Infrastructure.Tests.Scraping;

/// <summary>
/// The mocked <see cref="HttpMessageHandler"/> behind the T-2.1 request-shape
/// tests: serves a scripted sequence of canned responses through factories —
/// a fresh <see cref="HttpResponseMessage"/> per attempt, repeating the last
/// factory when the script is exhausted — because the Polly retry pipeline
/// disposes the transient responses it retries. Records every request the
/// Refit pipeline sends (method, URI, headers, body) for exact assertions.
/// </summary>
internal sealed class RecordingHttpMessageHandler(params Func<HttpResponseMessage>[] responseFactories) : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> scriptedFactories = new(responseFactories);
    private readonly Func<HttpResponseMessage> lastFactory = responseFactories.Length > 0
        ? responseFactories[0]
        : throw new InvalidOperationException("At least one canned response factory is required.");

    /// <summary>The requests received so far, in order (retries included).</summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>The deserialized request bodies, parallel to <see cref="Requests"/>.</summary>
    public List<string?> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        var factory = scriptedFactories.Count > 0 ? scriptedFactories.Dequeue() : lastFactory;
        var response = factory();

        // Refit's failure path (ApiException.Create) reads RequestMessage; canned
        // responses bypass HttpClient's automatic assignment, so set it explicitly.
        response.RequestMessage = request;
        return response;
    }
}
