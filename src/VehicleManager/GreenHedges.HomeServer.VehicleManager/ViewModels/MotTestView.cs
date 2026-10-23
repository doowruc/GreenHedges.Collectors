using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

/// <summary>An MOT test from the DVSA's history, with its defects and advisories.</summary>
public class MotTestView
{
    public int Id { get; init; }
    /// <summary>When it was completed (UTC); <see cref="Date"/> is the UK date.</summary>
    public DateTime CompletedDate { get; init; }
    public bool Passed { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public int? Odometer { get; init; }
    public bool OdometerInKilometres { get; init; }
    public string? MotTestNumber { get; init; }
    public string? RegistrationAtTest { get; init; }
    public string? DataSource { get; init; }
    public List<MotDefectView> Defects { get; init; } = [];

    /// <summary>
    /// When the vehicle was bought, if the test was before then (a previous owner's MOT); null otherwise. Worked out
    /// from the vehicle's purchase date by <see cref="MarkPreviousOwner"/>, not stored, so it follows changes to that date.
    /// </summary>
    public DateOnly? BoughtAfter { get; private set; }

    /// <summary>Notes the tests done before the vehicle was bought (none without a purchase date).</summary>
    public static void MarkPreviousOwner(IEnumerable<MotTestView> tests, DateOnly? purchasedDate)
    {
        foreach (var test in tests) test.BoughtAfter = purchasedDate is { } bought && test.Date < bought ? bought : null;
    }

    public DateOnly Date => Reminders.UkDate(CompletedDate);

    /// <summary>
    /// Newest first, by UK date. On the same day a pass comes after a failure (it's the retest), whatever the
    /// DVSA's times say: they can be identical, or even have the retest first.
    /// </summary>
    public static List<MotTestView> NewestFirst(IEnumerable<MotTestView> tests) =>
        [.. tests.OrderByDescending(t => t.Date).ThenByDescending(t => t.Passed).ThenByDescending(t => t.CompletedDate).ThenByDescending(t => t.Id)];

    /// <summary>Links a test's point on the mileage chart to its item in the list (<c>data-chart-ref</c>).</summary>
    public static string ChartRef(int id) => $"mot-{id}";

    /// <summary>E.g. "45,123 miles" or "72,000 km"; null if the odometer wasn't read.</summary>
    public string? OdometerText => Odometer is { } odometer ? $"{odometer:N0} {(OdometerInKilometres ? "km" : "miles")}" : null;

    /// <summary>The odometer reading in miles (converted from kilometres if need be), for the mileage trend.</summary>
    public int? Miles => Odometer is { } odometer ? MileageTrend.Miles(odometer, OdometerInKilometres) : null;

    /// <summary>Northern Ireland's tests come without defect details, so "no defects" can't be said of them.</summary>
    public bool HasDefectDetails => DataSource != "DVA NI";

    public static readonly Expression<Func<MotTest, MotTestView>> Projection = t => new MotTestView
    {
        Id = t.Id,
        CompletedDate = t.CompletedDate,
        Passed = t.Passed,
        ExpiryDate = t.ExpiryDate,
        Odometer = t.Odometer,
        OdometerInKilometres = t.OdometerInKilometres,
        MotTestNumber = t.MotTestNumber,
        RegistrationAtTest = t.RegistrationAtTest,
        DataSource = t.DataSource,
        Defects = t.Defects.OrderBy(d => d.Sequence).Select(d => new MotDefectView { Type = d.Type, Text = d.Text, Dangerous = d.Dangerous }).ToList()
    };
}
