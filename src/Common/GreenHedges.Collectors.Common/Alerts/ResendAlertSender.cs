using System.Net.Http.Headers;
using System.Net.Http.Json;
using GreenHedges.Collectors.Common.Hosting;
using Microsoft.Extensions.Options;

namespace GreenHedges.Collectors.Common.Alerts;

/// <summary>Sends alerts as plain-text email through the Resend API.</summary>
public sealed class ResendAlertSender(HttpClient http, IOptions<AlertOptions> options, CollectorIdentity identity) : IAlertSender
{
    private static readonly Uri EmailsEndpoint = new("https://api.resend.com/emails");

    private readonly AlertOptions _options = options.Value;

    public async Task SendAsync(string subject, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EmailsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);
        request.Headers.UserAgent.ParseAdd($"{identity.Name}/1.0");
        request.Content = JsonContent.Create(new ResendEmail(_options.From!, _options.To, subject, body));

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Resend returned HTTP {(int)response.StatusCode}: {error}",
                null,
                response.StatusCode);
        }
    }

    private sealed record ResendEmail(string From, string[] To, string Subject, string Text);
}
