using System.Globalization;
using Auth0.AspNetCore.Authentication;
using Coravel;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Resend;
using VehicleManager.Data;
using VehicleManager.Pages.FuelBrands;
using VehicleManager.Services;

var builder = WebApplication.CreateBuilder(args);

// Auth0 sign-in. The settings live in user secrets in development (see README), environment
// variables elsewhere, e.g. Auth0__Domain.
string RequiredSetting(string key) =>
    builder.Configuration[key] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException(
            $"{key} is not configured. Set it with: dotnet user-secrets set \"{key}\" \"<value>\"");

builder.Services.AddAuth0WebAppAuthentication(options =>
{
    options.Domain = RequiredSetting("Auth0:Domain");
    options.ClientId = RequiredSetting("Auth0:ClientId");
    options.ClientSecret = RequiredSetting("Auth0:ClientSecret");
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.Scope = "openid profile email";
    options.OpenIdConnectEvents = new OpenIdConnectEvents
    {
        // On sign-in, hand any data from before per-user data to the ExistingDataOwner (see README).
        OnTokenValidated = context => context.HttpContext.RequestServices
            .GetRequiredService<ExistingDataClaim>()
            .ClaimIfOwnerAsync(context.Principal!)
    };
});

// Every page requires a signed-in user whose verified email is on the allow-list, unless it opts
// out with [AllowAnonymous]. Signed-in users who aren't allowed are sent to Account/AccessDenied.
var allowedUsers = AllowedUsers.FromSetting(RequiredSetting("AllowedEmails"));
builder.Services.AddSingleton(allowedUsers);
var allowedUserPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .RequireAssertion(context => allowedUsers.IsAllowed(context.User))
    .Build();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AllowedUsers.PolicyName, allowedUserPolicy)
    .SetFallbackPolicy(allowedUserPolicy);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ExistingDataClaim>();
builder.Services.AddScoped<FuelStops>();
builder.Services.AddScoped<FuelBrandList>();
builder.Services.AddScoped<FuelStationList>();
// Azure SQL Database (see README). Retries cover Azure's brief transient faults, and a serverless
// database waking up from auto-pause.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(RequiredSetting("ConnectionStrings:VehicleManager"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddScoped<FuelStatsService>();
builder.Services.AddScoped<FuelImportService>();
builder.Services.AddScoped<BackupService>();

// Backups are emailed through Resend (see README). The sender's domain must be verified in Resend.
builder.Services.AddResend(options =>
{
    options.ApiToken = RequiredSetting("Resend:ApiKey");
    options.ThrowExceptions = true;
});
builder.Services.AddSingleton(new EmailOptions(RequiredSetting("Resend:From")));
builder.Services.AddScoped<BackupEmailer>();
builder.Services.AddScoped<ReminderEmailer>();

// MOT and tax lookups (see README). Both are optional: without their settings, those dates are typed in.
var dvlaOptions = DvlaOptions.From(builder.Configuration.GetSection("Dvla"));
builder.Services.AddSingleton(dvlaOptions);
builder.Services.AddHttpClient<DvlaClient>(http =>
{
    http.BaseAddress = new Uri(dvlaOptions.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(20);
});
var motHistoryOptions = MotHistoryOptions.From(builder.Configuration.GetSection("MotHistory"));
builder.Services.AddSingleton(motHistoryOptions);
builder.Services.AddSingleton<MotHistoryTokenCache>();
builder.Services.AddHttpClient<MotHistoryClient>(http =>
{
    http.BaseAddress = new Uri(motHistoryOptions.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<VehicleLookup>();

builder.Services.AddScheduler();
builder.Services.AddTransient<ScheduledBackups>();
builder.Services.AddTransient<ScheduledReminders>();

var app = builder.Build();

// Create the tables on first run and apply any pending migrations. The tests turn this off, so
// only the real app changes the database's schema.
if (app.Configuration.GetValue("MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.MigrateDatabase(app.Logger);
}

app.Services.UseScheduler(scheduler =>
    {
        // Nightly, email each user a backup if their data has changed since their last one.
        scheduler.Schedule<ScheduledBackups>()
            .DailyAt(1, 0)
            .Zoned(TimeZoneInfo.Utc)
            .PreventOverlapping(nameof(ScheduledBackups));
        // Each morning (UK time), update MOT and tax dates and email any reminders due.
        scheduler.Schedule<ScheduledReminders>()
            .DailyAt(7, 0)
            .Zoned(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"))
            .PreventOverlapping(nameof(ScheduledReminders));
    })
    .OnError(ex => app.Logger.LogError(ex, "A scheduled task failed"));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// UK formats: dd/MM/yyyy dates and £ currency.
var ukCulture = new CultureInfo("en-GB");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(ukCulture),
    SupportedCultures = [ukCulture],
    SupportedUICultures = [ukCulture]
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();

app.Run();

// Lets tests start the app in memory (WebApplicationFactory<Program>).
public partial class Program;
