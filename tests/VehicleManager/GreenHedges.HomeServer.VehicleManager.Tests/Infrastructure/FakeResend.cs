using System.Collections.Concurrent;
using System.Reflection;
using Resend;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>
/// Stands in for Resend, so tests never send real email: records the emails it's asked to send,
/// and fails for addresses containing "fail". Every other <see cref="IResend"/> method throws.
/// Tests share it, so each should use its own recipient address (<see cref="NewAddress"/>).
/// </summary>
public class FakeResend : DispatchProxy
{
    public static ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public static IResend Create() => Create<IResend, FakeResend>();

    public static string NewAddress(string prefix = "backup") => $"{prefix}-{Guid.NewGuid():N}@tests.invalid";

    /// <summary>The emails sent to <paramref name="address"/>.</summary>
    public static List<EmailMessage> SentTo(string address) =>
        Sent.Where(m => m.To.Any(to => to.Email == address)).ToList();

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != nameof(IResend.EmailSendAsync) || args?[0] is not EmailMessage message)
            throw new NotSupportedException($"FakeResend doesn't support {method?.Name}.");

        if (message.To.Any(to => to.Email.Contains("fail")))
            return Task.FromException<ResendResponse<Guid>>(new ResendException(System.Net.HttpStatusCode.UnprocessableEntity, default, "Test failure", null!));
        Sent.Enqueue(message);
        return Task.FromResult(new ResendResponse<Guid>(Guid.NewGuid(), null!));
    }
}
