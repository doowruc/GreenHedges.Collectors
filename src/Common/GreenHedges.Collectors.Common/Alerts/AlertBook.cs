using GreenHedges.Collectors.Common.Hosting;

namespace GreenHedges.Collectors.Common.Alerts;

/// <summary>
/// The open alerts and their emails: one PROBLEM email per incident (repeats suppressed until it
/// clears) and one RESOLVED email, only if the PROBLEM email went out. Open alerts are saved in
/// <see cref="AlertStateStore"/>, so a restart neither repeats nor loses an email.
/// </summary>
/// <remarks>
/// Keys identify the kind of alert and are what's saved, so a collector must keep them stable.
/// Deciding when to raise or resolve is the collector's job; see <see cref="AlertMonitorService"/>.
/// </remarks>
public sealed class AlertBook(
    IAlertSender sender,
    AlertStateStore store,
    CollectorIdentity identity,
    TimeProvider time,
    ILogger<AlertBook> logger)
{
    private readonly Dictionary<string, ActiveAlert> _active = [];

    private string SubjectPrefix => $"[{identity.Name}]";

    public bool IsOpen(string key) => _active.ContainsKey(key);

    /// <summary>Restores alerts that were open when the collector last stopped. Saved keys not in <paramref name="knownKeys"/> are dropped.</summary>
    public async Task LoadAsync(IReadOnlyCollection<string> knownKeys, CancellationToken cancellationToken)
    {
        _active.Clear();
        foreach (var (key, saved) in await store.LoadAsync(cancellationToken))
        {
            if (knownKeys.Contains(key))
            {
                _active[key] = new ActiveAlert(saved.Title, saved.Since) { Notified = saved.Notified };
                logger.LogInformation(
                    "Alert still open from before restart: {Title} (since {Since:u}, emailed: {Notified})",
                    saved.Title,
                    saved.Since,
                    saved.Notified);
            }
        }
    }

    /// <summary>Opens the alert if it isn't open, and sends its PROBLEM email if that hasn't gone out yet.</summary>
    public async Task RaiseAsync(string key, string title, string body, CancellationToken cancellationToken)
    {
        var changed = false;
        if (!_active.TryGetValue(key, out var alert))
        {
            alert = new ActiveAlert(title, time.GetUtcNow());
            _active[key] = alert;
            changed = true;
            logger.LogWarning("Alert raised: {Title}", title);
        }

        if (!alert.Notified)
        {
            // If sending fails, Notified stays false and the next check tries again.
            alert.Notified = await TrySendAsync($"{SubjectPrefix} PROBLEM: {title}", body, cancellationToken);
            changed |= alert.Notified;
        }

        if (changed)
        {
            await SaveAsync(cancellationToken);
        }
    }

    /// <summary>Closes the alert if it's open, with a RESOLVED email if its PROBLEM email went out.</summary>
    public async Task ResolveAsync(string key, CancellationToken cancellationToken)
    {
        if (!_active.Remove(key, out var alert))
        {
            return;
        }

        await SaveAsync(cancellationToken);
        var duration = time.GetUtcNow() - alert.Since;
        logger.LogInformation("Alert resolved after {Duration}: {Title}", duration, alert.Title);

        // Only send a recovery email if the problem email went out. Not retried if it fails.
        if (alert.Notified)
        {
            await TrySendAsync(
                $"{SubjectPrefix} RESOLVED: {alert.Title}",
                $"Resolved after {FormatDuration(duration)}. Collection is back to normal on {Environment.MachineName}.",
                cancellationToken);
        }
    }

    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{(int)duration.TotalMinutes}m";

    private Task SaveAsync(CancellationToken cancellationToken) =>
        store.SaveAsync(
            _active.ToDictionary(
                pair => pair.Key,
                pair => new PersistedAlert(pair.Value.Title, pair.Value.Since, pair.Value.Notified)),
            cancellationToken);

    private async Task<bool> TrySendAsync(string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(subject, body, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to send alert: {Subject}", subject);
            return false;
        }
    }

    private sealed class ActiveAlert(string title, DateTimeOffset since)
    {
        public string Title { get; } = title;

        public DateTimeOffset Since { get; } = since;

        public bool Notified { get; set; }
    }
}
