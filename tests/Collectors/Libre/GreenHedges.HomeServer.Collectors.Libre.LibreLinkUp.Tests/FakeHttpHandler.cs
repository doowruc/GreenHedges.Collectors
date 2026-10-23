using System.Net;
using System.Text;

namespace GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp.Tests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    public string? Header(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>Replies with queued responses in order and records each request (headers and body snapshotted).</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpHandler Reply(string json, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}");
        }

        return _responses.Dequeue()();
    }
}
