using System.Diagnostics;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GreenHedges.HomeServer.Common.Hosting;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Common.Backups;

public sealed class OffsiteOptions
{
    public const string SectionName = "Offsite";

    /// <summary>S3 bucket. Off-site copies are off when empty.</summary>
    public string? Bucket { get; set; }

    /// <summary>AWS region of the bucket, e.g. <c>eu-west-2</c>. Also the signing region for S3-compatible services.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// Key prefix ("folder") inside the bucket; the collector's name in lower case when not set. The
    /// write-only IAM policy should be scoped to it.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>Access key for an IAM user that may only <c>s3:PutObject</c> under the prefix. If empty, the AWS default credential chain is used.</summary>
    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    /// <summary>Optional endpoint for S3-compatible storage (Backblaze B2, Cloudflare R2, MinIO, …). Empty = AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Storage class for uploads. Move older copies to Glacier Deep Archive with a bucket lifecycle rule rather than here.</summary>
    public string StorageClass { get; set; } = "STANDARD";

    /// <summary>The <c>age</c> public key (<c>age1…</c>) backups are encrypted to. The private key never goes on the server.</summary>
    public string? AgeRecipient { get; set; }

    /// <summary>The <c>age</c> executable.</summary>
    public string AgePath { get; set; } = "age";

    /// <summary>How often to check for a local backup that hasn't been copied yet (also the retry interval).</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Raise the "Off-site backup failing" alert if there's been no successful copy for this long.</summary>
    public TimeSpan AlertAfter { get; set; } = TimeSpan.FromHours(36);

    public bool Enabled => !string.IsNullOrWhiteSpace(Bucket);

    /// <summary>What's wrong with the settings when enabled, or null if they're usable.</summary>
    public string? Problem()
    {
        if (!Enabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(Region))
        {
            return "Offsite:Region is not set.";
        }

        if (AgeRecipient?.StartsWith("age1", StringComparison.Ordinal) != true)
        {
            return "Offsite:AgeRecipient must be an age public key (age1…).";
        }

        if (string.IsNullOrWhiteSpace(AccessKeyId) != string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            return "Offsite:AccessKeyId and Offsite:SecretAccessKey must be set together.";
        }

        return null;
    }
}

/// <summary>Off-site copies are enabled but the settings can't work. Alerted immediately; collection carries on.</summary>
public sealed class OffsiteMisconfiguredException(string message) : Exception(message);

public interface IBackupEncryptor
{
    /// <summary>Encrypts <paramref name="inputPath"/> to <paramref name="outputPath"/> (overwritten).</summary>
    Task EncryptAsync(string inputPath, string outputPath, CancellationToken cancellationToken);
}

/// <summary>Encrypts with the <c>age</c> command-line tool to a single public-key recipient.</summary>
public sealed class AgeEncryptor(string agePath, string recipient) : IBackupEncryptor
{
    public async Task EncryptAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(agePath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--encrypt");
        start.ArgumentList.Add("--recipient");
        start.ArgumentList.Add(recipient);
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(outputPath);
        start.ArgumentList.Add(inputPath);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Couldn't start '{agePath}'.");
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"age exited with code {process.ExitCode}: {(await stderr).Trim()}");
        }
    }
}

public interface IOffsiteStore
{
    /// <summary>Uploads <paramref name="filePath"/> as <paramref name="key"/>. Write-only: no read, list or delete is needed.</summary>
    Task PutAsync(string key, string filePath, CancellationToken cancellationToken);

    /// <summary>For log messages, e.g. <c>s3://bucket</c>.</summary>
    string Describe(string key);
}

public sealed class S3OffsiteStore : IOffsiteStore, IDisposable
{
    private readonly OffsiteOptions _options;
    private readonly AmazonS3Client _client;

    public S3OffsiteStore(OffsiteOptions options)
    {
        _options = options;
        var config = new AmazonS3Config();
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            // S3-compatible services: path-style addressing, sign for the configured region, and only
            // send checksums when required (not every provider supports the newer default checksums).
            config.ServiceURL = options.ServiceUrl;
            config.ForcePathStyle = true;
            config.AuthenticationRegion = options.Region;
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
        }

        _client = string.IsNullOrWhiteSpace(options.AccessKeyId)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
    }

    public async Task PutAsync(string key, string filePath, CancellationToken cancellationToken)
    {
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                FilePath = filePath,
                ContentType = "application/octet-stream",
                StorageClass = S3StorageClass.FindValue(_options.StorageClass),
            },
            cancellationToken);
    }

    public string Describe(string key) => $"s3://{_options.Bucket}/{key}";

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Copies the newest local backup off-site: encrypt with <see cref="IBackupEncryptor"/>, upload with
/// <see cref="IOffsiteStore"/>. A marker file in the backups folder remembers the last copied backup,
/// because the write-only credentials can't check what the bucket already holds.
/// </summary>
public sealed class OffsiteCopier(
    string backupDirectory,
    string filePrefix,
    string prefix,
    IBackupEncryptor encryptor,
    IOffsiteStore store,
    BackupHealth health,
    TimeProvider time,
    ILogger logger)
{
    public const string MarkerFileName = ".offsite-last";

    /// <summary>Copies the newest backup if it hasn't been copied yet. Returns true if it uploaded something. Never throws (except on shutdown).</summary>
    public async Task<bool> CopyNewestAsync(CancellationToken cancellationToken)
    {
        var newest = BackupService.ListBackups(backupDirectory, filePrefix).FirstOrDefault();
        if (newest is null)
        {
            return false;
        }

        var name = Path.GetFileName(newest.Path);
        var marker = Path.Combine(backupDirectory, MarkerFileName);
        if (File.Exists(marker) && (await File.ReadAllTextAsync(marker, cancellationToken)).Trim() == name)
        {
            // Already copied: counts as healthy since the copy was made.
            health.RecordOffsite(new DateTimeOffset(File.GetLastWriteTimeUtc(marker), TimeSpan.Zero));
            return false;
        }

        var key = (string.IsNullOrWhiteSpace(prefix) ? "" : prefix.Trim('/') + "/") + name + ".age";
        var encrypted = Path.Combine(backupDirectory, name + ".age.tmp");
        try
        {
            await encryptor.EncryptAsync(newest.Path, encrypted, cancellationToken);
            var size = new FileInfo(encrypted).Length;
            await store.PutAsync(key, encrypted, cancellationToken);
            await File.WriteAllTextAsync(marker, name, cancellationToken);
            health.RecordOffsite(time.GetUtcNow());
            logger.LogInformation("Off-site copy uploaded: {Destination} ({SizeKiB:N0} KiB)", store.Describe(key), size / 1024);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            health.RecordOffsiteFailure(ex);
            logger.LogError(ex, "Off-site copy of {Backup} failed", name);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(encrypted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Couldn't delete {Path}", encrypted);
            }
        }
    }
}

/// <summary>Runs <see cref="OffsiteCopier"/> at startup and every <see cref="OffsiteOptions.CheckInterval"/>.</summary>
public sealed class OffsiteBackupService(
    IServiceProvider services,
    IOptions<OffsiteOptions> options,
    IOptions<BackupOptions> backupOptions,
    BackupHealth health,
    CollectorIdentity identity,
    TimeProvider time,
    ILogger<OffsiteBackupService> logger) : BackgroundService
{
    private readonly OffsiteOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !backupOptions.Value.Enabled)
        {
            logger.LogInformation("Off-site backup is off (Offsite:Bucket not set, or local backups disabled)");
            return;
        }

        // A bad off-site setting must never stop collection: report it and stand down.
        if (_options.Problem() is { } problem)
        {
            logger.LogError("Off-site backup is misconfigured and won't run: {Problem}", problem);
            health.RecordOffsiteFailure(new OffsiteMisconfiguredException(problem));
            return;
        }

        var prefix = _options.Prefix ?? identity.Name.ToLowerInvariant();
        var copier = new OffsiteCopier(
            Path.GetFullPath(backupOptions.Value.Directory),
            BackupService.FilePrefix(identity),
            prefix,
            services.GetRequiredService<IBackupEncryptor>(),
            services.GetRequiredService<IOffsiteStore>(),
            health,
            time,
            logger);
        logger.LogInformation("Off-site backup to {Destination}", services.GetRequiredService<IOffsiteStore>().Describe(prefix));

        while (!stoppingToken.IsCancellationRequested)
        {
            await copier.CopyNewestAsync(stoppingToken);
            try
            {
                await Task.Delay(_options.CheckInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
