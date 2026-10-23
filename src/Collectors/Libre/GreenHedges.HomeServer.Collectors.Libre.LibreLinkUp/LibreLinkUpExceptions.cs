using System.Net;

namespace GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;

public class LibreLinkUpException(
    string message,
    int? apiStatus = null,
    HttpStatusCode? httpStatus = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>The <c>status</c> field of the API response, if one was returned.</summary>
    public int? ApiStatus { get; } = apiStatus;

    public HttpStatusCode? HttpStatus { get; } = httpStatus;
}

/// <summary>
/// The account needs someone to step in (credentials, terms, app version, sharing).
/// Retrying straight away won't help and may lock the account.
/// </summary>
public abstract class LibreLinkUpUserActionRequiredException(
    string message,
    int? apiStatus = null,
    HttpStatusCode? httpStatus = null) : LibreLinkUpException(message, apiStatus, httpStatus);

public sealed class BadCredentialsException(int apiStatus, HttpStatusCode httpStatus)
    : LibreLinkUpUserActionRequiredException("LibreLinkUp rejected the email or password.", apiStatus, httpStatus);

/// <summary>
/// Login returned <c>status: 4</c> with a step to complete, such as <c>tou</c> (re-accept the Terms of Use).
/// To clear it, sign in to the LibreLinkUp app with the follower account.
/// </summary>
public sealed class LoginStepRequiredException(string stepType, HttpStatusCode httpStatus)
    : LibreLinkUpUserActionRequiredException(
        $"LibreLinkUp requires the '{stepType}' step to be completed. Sign in to the LibreLinkUp app with the follower account to complete it.",
        4,
        httpStatus)
{
    public string StepType { get; } = stepType;
}

/// <summary>The configured <c>version</c> header is below the minimum the API accepts (status 920).</summary>
public sealed class UnsupportedVersionException(string configuredVersion, string? minimumVersion, HttpStatusCode httpStatus)
    : LibreLinkUpUserActionRequiredException(
        $"LibreLinkUp rejected app version {configuredVersion}; minimum is {minimumVersion ?? "unknown"}. Update LibreLinkUp:Version.",
        920,
        httpStatus)
{
    public string ConfiguredVersion { get; } = configuredVersion;

    public string? MinimumVersion { get; } = minimumVersion;
}

/// <summary>The follower account has no connections (the sharing invitation isn't accepted, or was removed).</summary>
public sealed class NoConnectionsException()
    : LibreLinkUpUserActionRequiredException("The LibreLinkUp follower account has no connections. Check that the sharing invitation has been accepted.");

public sealed class RateLimitedException(TimeSpan? retryAfter, int? apiStatus = null)
    : LibreLinkUpException(
        $"LibreLinkUp rate limit hit; retry after {retryAfter?.ToString() ?? "unspecified"}.",
        apiStatus,
        HttpStatusCode.TooManyRequests)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
