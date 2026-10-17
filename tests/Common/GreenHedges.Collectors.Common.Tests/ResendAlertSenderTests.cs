using System.Net;
using System.Text;
using System.Text.Json;
using GreenHedges.Collectors.Common.Alerts;
using GreenHedges.Collectors.Common.Hosting;
using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Common.Tests;

public sealed class ResendAlertSenderTests
{
    private readonly CapturingHandler _handler = new();

    private ResendAlertSender CreateSender() => new(
        new HttpClient(_handler),
        Options.Create(new AlertOptions
        {
            ResendApiKey = "re_test_key",
            From = "LibreCollector <alerts@example.com>",
            To = ["me@example.com"],
        }),
        new CollectorIdentity("TestCollector"));

    [Fact]
    public async Task Posts_plain_text_email_to_resend()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "id": "abc" }""", Encoding.UTF8, "application/json"),
        };

        await CreateSender().SendAsync("Subject line", "Body text", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, _handler.Method);
        Assert.Equal("https://api.resend.com/emails", _handler.Uri?.ToString());
        Assert.Equal("Bearer re_test_key", _handler.Authorization);
        Assert.Equal("TestCollector/1.0", _handler.UserAgent);

        using var body = JsonDocument.Parse(_handler.Body!);
        var root = body.RootElement;
        Assert.Equal("LibreCollector <alerts@example.com>", root.GetProperty("from").GetString());
        Assert.Equal("me@example.com", Assert.Single(root.GetProperty("to").EnumerateArray()).GetString());
        Assert.Equal("Subject line", root.GetProperty("subject").GetString());
        Assert.Equal("Body text", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Error_response_throws_with_details()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "message": "The example.com domain is not verified." }"""),
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateSender().SendAsync("s", "b", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("domain is not verified", ex.Message);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public string? Authorization { get; private set; }

        public string? UserAgent { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }
}
