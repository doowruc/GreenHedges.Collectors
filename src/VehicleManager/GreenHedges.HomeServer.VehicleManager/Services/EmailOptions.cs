namespace VehicleManager.Services;

/// <summary>The address backup and reminder emails come from (<c>Resend:From</c>); its domain must be verified in Resend.</summary>
public sealed record EmailOptions(string From);
