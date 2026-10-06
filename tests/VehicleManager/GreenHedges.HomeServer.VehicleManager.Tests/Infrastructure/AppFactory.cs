using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VehicleManager.Tests.Infrastructure;

/// <summary>
/// Runs the app in memory against the test database. Auth0 is replaced by a test sign-in: a
/// request is signed in as the user named in its <see cref="UserHeader"/> (see <see cref="ClientFor"/>),
/// with an allowed, verified email; a request without it isn't signed in.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string UserHeader = "X-Test-User";
    public const string Email = "tests@vehiclemanager.invalid";

    static AppFactory()
    {
        // Environment variables override the app's user secrets. Auth0 is never called (the test
        // sign-in replaces it), but the app needs settings to start.
        Environment.SetEnvironmentVariable("AllowedEmails", Email);
        Environment.SetEnvironmentVariable("Auth0__Domain", "tests.invalid");
        Environment.SetEnvironmentVariable("Auth0__ClientId", "tests");
        Environment.SetEnvironmentVariable("Auth0__ClientSecret", "tests");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestSignIn>(TestSignIn.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = o.DefaultForbidScheme = TestSignIn.SchemeName;
            });
        });
    }

    /// <summary>A client signed in as <paramref name="userId"/>, or not signed in if null. Doesn't follow redirects.</summary>
    public PageClient ClientFor(string? userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userId is not null) client.DefaultRequestHeaders.Add(UserHeader, userId);
        return new PageClient(client);
    }

    private sealed class TestSignIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim("email", Email),
                new Claim("email_verified", "true")
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
