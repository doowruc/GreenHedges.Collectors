namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>What the fake DVLA and DVSA know about a vehicle. Null dates are left out of the responses.</summary>
public sealed record FakeVehicle
{
    public string TaxStatus { get; init; } = "Taxed";
    public DateOnly? TaxDueDate { get; init; }
    /// <summary>The DVLA's MOT expiry.</summary>
    public DateOnly? DvlaMotExpiryDate { get; init; }
    /// <summary>Expiry dates of the DVSA's passed MOT tests (a failed test, with no expiry, is added too).</summary>
    public DateOnly[] MotTestExpiryDates { get; init; } = [];
    /// <summary>For a vehicle too new for an MOT: when the first one's due, per the DVSA.</summary>
    public DateOnly? FirstMotDueDate { get; init; }
    public bool AtDvla { get; init; } = true;
    public bool AtDvsa { get; init; } = true;
    public bool DvlaFails { get; init; }
}
