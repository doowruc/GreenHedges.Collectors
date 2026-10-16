namespace VehicleManager.Services;

/// <summary>A vehicle's MOT history. A vehicle too new for an MOT has no tests, but a first-MOT due date.</summary>
public sealed record MotHistoryVehicle(string? Registration, DateOnly? MotTestDueDate, List<MotHistoryTest>? MotTests)
{
    /// <summary>When the MOT runs out: the latest test certificate's expiry, or the first MOT's due date.</summary>
    public DateOnly? MotDueDate => MotTests?.Max(t => t.ExpiryDate) ?? MotTestDueDate;
}
