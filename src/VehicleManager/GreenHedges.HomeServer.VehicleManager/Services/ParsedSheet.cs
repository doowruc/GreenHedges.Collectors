using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record ParsedSheet(string Name, List<FuelEntryInput> Entries, List<string> Warnings)
{
    public DateOnly? From => Entries.Count > 0 ? Entries.Min(e => e.Date) : null;
    public DateOnly? To => Entries.Count > 0 ? Entries.Max(e => e.Date) : null;
    public decimal TotalLitres => Entries.Sum(e => e.Litres);
    public decimal TotalCost => Entries.Sum(e => e.TotalCost);
}
