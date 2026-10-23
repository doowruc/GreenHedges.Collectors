namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

/// <summary>
/// A page header's Back button (<c>Pages/Shared/_BackButton</c>), linking to <paramref name="Href"/>. With
/// <paramref name="History"/>, it goes back to the previous page instead when that was another page of this app
/// (for a page reached from several places), and only follows the link otherwise.
/// </summary>
public sealed record BackButton(string Href, bool History = false);
