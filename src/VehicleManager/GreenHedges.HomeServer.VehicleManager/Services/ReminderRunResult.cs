namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record ReminderRunResult(int VehiclesUpdated, int LookupProblems, ReminderEmailResult Email);
