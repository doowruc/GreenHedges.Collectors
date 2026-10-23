namespace VehicleManager.Services;

/// <summary>What the DVLA holds about a vehicle's tax and MOT. Dates are YYYY-MM-DD in the response.</summary>
public sealed record DvlaVehicle(string? RegistrationNumber, string? TaxStatus, DateOnly? TaxDueDate, string? MotStatus, DateOnly? MotExpiryDate)
{
    /// <summary>Declared off the road, so no tax is due.</summary>
    public bool IsSorn => TaxStatus == "SORN";
}
