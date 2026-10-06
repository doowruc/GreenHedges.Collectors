using System.Security.Claims;

namespace VehicleManager.Services;

/// <summary>The signed-in user for the current request, identified by their Auth0 user id.</summary>
public class CurrentUser(IHttpContextAccessor httpContextAccessor)
{
    /// <summary>The current user's id, or null outside a signed-in request.</summary>
    public virtual string? Id => IdOf(httpContextAccessor.HttpContext?.User);

    /// <summary>The Auth0 user id (<c>sub</c> claim), which ASP.NET maps to <see cref="ClaimTypes.NameIdentifier"/>.</summary>
    public static string? IdOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? (user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub"))?.Value
            : null;
}
