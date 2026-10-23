namespace VehicleManager.Services;

/// <summary>A file that can't be restored; the message says why, for showing to the user.</summary>
public sealed class BackupFormatException(string message) : Exception(message);
