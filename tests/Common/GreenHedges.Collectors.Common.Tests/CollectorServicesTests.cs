using GreenHedges.Collectors.Common.Alerts;
using GreenHedges.Collectors.Common.Backups;
using GreenHedges.Collectors.Common.Heartbeat;
using GreenHedges.Collectors.Common.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GreenHedges.Collectors.Common.Tests;

public sealed class CollectorServicesTests
{
    [Fact]
    public void Shared_services_resolve()
    {
        var builder = CollectorHost.CreateBuilder([], "TestCollector");
        builder.AddAlerting().AddHeartbeat().AddBackups<NoBackupSource>();
        builder.Services.AddSingleton<IHeartbeatSource, NeverHealthy>();
        using var host = builder.Build();

        var hosted = host.Services.GetServices<IHostedService>().Select(s => s.GetType()).ToList();
        Assert.Contains(typeof(HeartbeatService), hosted);
        Assert.Contains(typeof(BackupService), hosted);
        Assert.Contains(typeof(OffsiteBackupService), hosted);
        Assert.IsType<LoggingAlertSender>(host.Services.GetRequiredService<IAlertSender>());
        Assert.NotNull(host.Services.GetRequiredService<AlertBook>());
        Assert.NotNull(host.Services.GetRequiredService<BackupMonitor>());
        Assert.Equal("TestCollector", host.Services.GetRequiredService<CollectorIdentity>().Name);
    }

    private sealed class NoBackupSource : IBackupSource
    {
        public Task BackupToAsync(string destinationPath, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NeverHealthy : IHeartbeatSource
    {
        public string? HealthyStatus(DateTimeOffset now, TimeSpan interval) => null;
    }
}
