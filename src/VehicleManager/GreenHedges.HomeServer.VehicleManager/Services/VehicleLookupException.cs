namespace VehicleManager.Services;

/// <summary>A lookup service refused a request or failed; the message says why, for showing to the user.</summary>
public sealed class VehicleLookupException(string message) : Exception(message);
