namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>What a lookup checks: the MOT, the tax, or both.</summary>
[Flags]
public enum LookupParts
{
    Mot = 1,
    Tax = 2,
    All = Mot | Tax
}
