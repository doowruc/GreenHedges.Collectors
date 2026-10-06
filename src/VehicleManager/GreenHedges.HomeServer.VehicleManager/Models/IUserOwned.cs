namespace VehicleManager.Models;

/// <summary>
/// Data that belongs to one signed-in user. <c>AppDbContext</c> only returns the current user's
/// rows and stamps new rows with their id.
/// </summary>
public interface IUserOwned
{
    /// <summary>The Auth0 user id (the <c>sub</c> claim), e.g. <c>auth0|abc123</c>. Empty for unclaimed data.</summary>
    string UserId { get; set; }
}
