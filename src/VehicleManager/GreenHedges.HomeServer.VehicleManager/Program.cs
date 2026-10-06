using System.Globalization;
using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using VehicleManager.Data;
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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("VehicleManager")));
builder.Services.AddScoped<FuelStatsService>();
builder.Services.AddScoped<FuelImportService>();

var app = builder.Build();

// Create the SQLite database on first run and apply any pending migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.MigrateDatabase(app.Logger);
}

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
