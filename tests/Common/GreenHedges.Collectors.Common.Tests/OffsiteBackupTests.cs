using System.Diagnostics;
using GreenHedges.Collectors.Common.Backups;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.Collectors.Common.Tests;

public sealed class OffsiteBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"librecollector-offsite-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 3, 31, 0, TimeSpan.Zero));
    private readonly BackupHealth _health;
    private readonly FakeEncryptor _encryptor = new();
    private readonly FakeStore _store = new();

    public OffsiteBackupTests()
    {
        Directory.CreateDirectory(_dir);
        _health = new BackupHealth(_time);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private OffsiteCopier Copier(string prefix = "librecollector") =>
        new(_dir, "librecollector-", prefix, _encryptor, _store, _health, _time, NullLogger.Instance);

    private string Backup(string stamp, string content = "backup")
    {
        var path = Path.Combine(_dir, $"librecollector-{stamp}.db.gz");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Copies_the_newest_backup_once()
    {
        Backup("20261016T023000Z", "older");
        Backup("20261017T023000Z", "newest");

        Assert.True(await Copier().CopyNewestAsync(CancellationToken.None));
        Assert.False(await Copier().CopyNewestAsync(CancellationToken.None));

        var (key, content) = Assert.Single(_store.Uploads);
        Assert.Equal("librecollector/librecollector-20261017T023000Z.db.gz.age", key);
        Assert.Equal("ENCRYPTED(newest)", content);
        Assert.Equal(_time.GetUtcNow(), _health.Snapshot().LastOffsiteAt);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task A_new_backup_is_copied_after_the_previous_one()
    {
        Backup("20261017T023000Z");
        await Copier().CopyNewestAsync(CancellationToken.None);
        Backup("20261018T023000Z");

        Assert.True(await Copier().CopyNewestAsync(CancellationToken.None));
        Assert.Equal(
            ["librecollector/librecollector-20261017T023000Z.db.gz.age", "librecollector/librecollector-20261018T023000Z.db.gz.age"],
            _store.Uploads.Select(u => u.Key));
    }

    [Fact]
    public async Task Failure_is_recorded_and_retried_next_time()
    {
        Backup("20261017T023000Z");
        _store.Fail = true;

        Assert.False(await Copier().CopyNewestAsync(CancellationToken.None));
        Assert.NotNull(_health.Snapshot().LastOffsiteError);
        Assert.Null(_health.Snapshot().LastOffsiteAt);
        Assert.False(File.Exists(Path.Combine(_dir, OffsiteCopier.MarkerFileName)));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));

        _store.Fail = false;
        Assert.True(await Copier().CopyNewestAsync(CancellationToken.None));
        Assert.Null(_health.Snapshot().LastOffsiteError);
    }

    [Fact]
    public async Task Nothing_to_do_without_backups()
    {
        Assert.False(await Copier().CopyNewestAsync(CancellationToken.None));
        Assert.Empty(_store.Uploads);
    }

    [Fact]
    public async Task Empty_prefix_uploads_to_the_bucket_root()
    {
        Backup("20261017T023000Z");
        await Copier(prefix: "").CopyNewestAsync(CancellationToken.None);
        Assert.Equal("librecollector-20261017T023000Z.db.gz.age", Assert.Single(_store.Uploads).Key);
    }

    [Fact]
    public async Task Age_encryptor_round_trip_with_the_real_age_tool()
    {
        // Requires the age CLI (brew install age / apt install age). CI installs it.
        var keyFile = Path.Combine(_dir, "test.key");
        var recipient = Run("age-keygen", "-o", keyFile).StdErr.Trim().Replace("Public key: ", "");
        Assert.StartsWith("age1", recipient);

        var plain = Path.Combine(_dir, "plain.bin");
        var encrypted = Path.Combine(_dir, "plain.bin.age");
        var decrypted = Path.Combine(_dir, "decrypted.bin");
        var bytes = new byte[100_000];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(plain, bytes);

        await new AgeEncryptor("age", recipient).EncryptAsync(plain, encrypted, CancellationToken.None);
        Assert.NotEqual(bytes, await File.ReadAllBytesAsync(encrypted));

        Run("age", "--decrypt", "--identity", keyFile, "--output", decrypted, encrypted);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(decrypted));
    }

    [Fact]
    public async Task Age_encryptor_reports_a_bad_recipient()
    {
        var plain = Path.Combine(_dir, "plain.bin");
        await File.WriteAllTextAsync(plain, "x");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgeEncryptor("age", "age1notavalidkey").EncryptAsync(plain, plain + ".age", CancellationToken.None));
        Assert.Contains("age exited", ex.Message);
    }

    private static (string StdOut, string StdErr) Run(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{file} failed: {stderr}");
        return (stdout, stderr);
    }

    private sealed class FakeEncryptor : IBackupEncryptor
    {
        public async Task EncryptAsync(string inputPath, string outputPath, CancellationToken cancellationToken) =>
            await File.WriteAllTextAsync(outputPath, $"ENCRYPTED({await File.ReadAllTextAsync(inputPath, cancellationToken)})", cancellationToken);
    }

    private sealed class FakeStore : IOffsiteStore
    {
        public List<(string Key, string Content)> Uploads { get; } = [];

        public bool Fail { get; set; }

        public async Task PutAsync(string key, string filePath, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("S3 unreachable");
            }

            Uploads.Add((key, await File.ReadAllTextAsync(filePath, cancellationToken)));
        }

        public string Describe(string key) => $"fake://{key}";
    }
}

public sealed class OffsiteOptionsTests
{
    private static OffsiteOptions Valid() => new()
    {
        Bucket = "bucket",
        Region = "eu-west-2",
        AgeRecipient = "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
        AccessKeyId = "AKIAEXAMPLE",
        SecretAccessKey = "secret",
    };

    [Fact]
    public void Disabled_or_valid_settings_have_no_problem()
    {
        Assert.Null(new OffsiteOptions().Problem());
        Assert.Null(Valid().Problem());
    }

    [Fact]
    public void Each_misconfiguration_is_reported()
    {
        var noRegion = Valid(); noRegion.Region = "";
        var badRecipient = Valid(); badRecipient.AgeRecipient = "AGE-SECRET-KEY-1ABC";
        var halfKey = Valid(); halfKey.SecretAccessKey = "";

        Assert.Contains("Region", noRegion.Problem());
        Assert.Contains("AgeRecipient", badRecipient.Problem());
        Assert.Contains("together", halfKey.Problem());
    }
}
