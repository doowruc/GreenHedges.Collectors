using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>Keeps a vehicle's stored MOT tests in step with the DVSA's MOT history.</summary>
public static class MotTests
{
    /// <summary>
    /// Makes the vehicle's stored tests match the DVSA's (without saving): new tests are added, changed ones
    /// updated in place, and ones the DVSA no longer has removed. Tests are matched by completion time and test number.
    /// A test without a completion time (possible for commercial vehicles) is skipped.
    /// </summary>
    public static async Task SyncAsync(AppDbContext db, Vehicle vehicle, IEnumerable<MotHistoryTest> fromDvsa)
    {
        // A vehicle that hasn't been saved has no stored tests to look up.
        List<MotTest> unmatched = vehicle.Id == 0 ? [] : await db.MotTests
            .Include(t => t.Defects)
            .Where(t => t.VehicleId == vehicle.Id)
            .ToListAsync();

        foreach (var test in fromDvsa)
        {
            if (test.CompletedDate is not { } completed) continue;
            var completedDate = completed.UtcDateTime;
            var number = Truncate(test.MotTestNumber, MotTest.MaxTestNumberLength);

            var stored = unmatched.FirstOrDefault(t => t.CompletedDate == completedDate && t.MotTestNumber == number);
            if (stored is null)
            {
                stored = new MotTest { CompletedDate = completedDate, MotTestNumber = number };
                vehicle.MotTests.Add(stored);
            }
            else unmatched.Remove(stored);
            Copy(test, stored);
        }

        db.MotTests.RemoveRange(unmatched);
    }

    /// <summary>Copies a test's details. Unchanged values leave the row unmodified, so the audit columns only change with the data.</summary>
    private static void Copy(MotHistoryTest from, MotTest to)
    {
        to.Passed = from.TestResult == "PASSED";
        to.ExpiryDate = from.ExpiryDate;
        to.Odometer = from.OdometerResultType is null or "READ" && int.TryParse(from.OdometerValue, out var odometer) ? odometer : null;
        to.OdometerInKilometres = to.Odometer is not null && from.OdometerUnit == "KM";
        to.RegistrationAtTest = Truncate(from.RegistrationAtTimeOfTest, 15);
        to.DataSource = Truncate(from.DataSource, MotTest.MaxDataSourceLength);

        // Defects are updated in place by position (rather than replaced), so an unchanged list changes nothing.
        var defects = (from.Defects ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Text)).ToList();
        var stored = to.Defects.OrderBy(d => d.Sequence).ToList();
        for (var i = 0; i < defects.Count; i++)
        {
            if (i == stored.Count)
            {
                stored.Add(new MotDefect { Sequence = i + 1 });
                to.Defects.Add(stored[i]);
            }
            stored[i].Sequence = i + 1;
            stored[i].Type = Truncate(defects[i].Type, MotDefect.MaxTypeLength);
            stored[i].Text = Truncate(defects[i].Text!.Trim(), MotDefect.MaxTextLength)!;
            stored[i].Dangerous = defects[i].Dangerous == true || defects[i].Type == "DANGEROUS";
        }
        foreach (var extra in stored.Skip(defects.Count)) to.Defects.Remove(extra);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
