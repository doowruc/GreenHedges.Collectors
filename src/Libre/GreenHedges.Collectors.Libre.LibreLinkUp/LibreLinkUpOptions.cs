namespace GreenHedges.Collectors.Libre.LibreLinkUp;

public sealed class LibreLinkUpOptions
{
    public const string SectionName = "LibreLinkUp";

    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>
    /// Region code such as <c>eu</c>, <c>eu2</c> or <c>us</c>. Optional: if not set, the global
    /// endpoint is used and the login redirect discovers the region.
    /// </summary>
    public string? Region { get; set; }

    public string Product { get; set; } = "llu.android";

    /// <summary>
    /// App version sent in the <c>version</c> header. Abbott enforces a minimum (API status 920),
    /// so this has to be raised when they retire older app versions.
    /// </summary>
    public string Version { get; set; } = "5.0.1";

    /// <summary>Log in again this long before the auth ticket expires.</summary>
    public TimeSpan TokenRefreshMargin { get; set; } = TimeSpan.FromMinutes(5);
}
