namespace VehicleManager.Services;

/// <summary>
/// A test as the DVSA sends it. <see cref="OdometerResultType"/> is READ, UNREADABLE or NO_ODOMETER; Northern
/// Ireland's tests (<see cref="DataSource"/> "DVA NI") come without defects.
/// </summary>
public sealed record MotHistoryTest(
    DateTimeOffset? CompletedDate, string? TestResult, DateOnly? ExpiryDate, string? OdometerValue, string? OdometerUnit,
    string? OdometerResultType, string? MotTestNumber, string? RegistrationAtTimeOfTest, string? DataSource, List<MotHistoryDefect>? Defects);
