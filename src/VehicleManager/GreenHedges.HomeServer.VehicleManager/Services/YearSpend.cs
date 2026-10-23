namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// A calendar year's fuel spend, all vehicles together. <see cref="Cumulative"/> is the running total at the end of
/// each month, January to December; months still to come in the current year are null.
/// <see cref="Change"/> is the change from the year before (0.1 = 10% more), null for the first year or when nothing
/// was spent the year before. For the current year (<see cref="ToDate"/> set) both years are compared up to the same
/// day, so a part year isn't set against a whole one.
/// </summary>
public record YearSpend(int Year, decimal Total, int FillUps, decimal? Change, DateOnly? ToDate, List<decimal?> Cumulative);
