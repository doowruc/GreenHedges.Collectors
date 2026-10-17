namespace GreenHedges.Collectors.Libre.Worker.Alerts;

public interface IAlertSender
{
    /// <summary>Sends an alert. Throws if it couldn't be delivered, so the caller can retry.</summary>
    Task SendAsync(string subject, string body, CancellationToken cancellationToken);
}

/// <summary>Used when no Resend API key is configured: alerts go to the log only.</summary>
public sealed class LoggingAlertSender(ILogger<LoggingAlertSender> logger) : IAlertSender
{
    public Task SendAsync(string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogWarning("Alert (email not configured): {Subject}\n{Body}", subject, body);
        return Task.CompletedTask;
    }
}
