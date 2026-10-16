using System.Linq.Expressions;
using VehicleManager.Models;
using VehicleManager.Services;

namespace VehicleManager.ViewModels;

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

    public DateOnly Date => Reminders.UkDate(CompletedDate);

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

public class MotDefectView
{
    public string? Type { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool Dangerous { get; init; }

    /// <summary>The badge for the defect's type, e.g. "Advisory".</summary>
    public string Label => Type switch
    {
        "ADVISORY" => "Advisory",
        "MINOR" => "Minor",
        "MAJOR" => "Major",
        "DANGEROUS" => "Dangerous",
        "FAIL" => "Fail",
        "PRS" => "Fixed at test",
        _ => "Note"
    };

    /// <summary>Bootstrap badge classes: red for failures, amber for minor defects and repairs made at the test, grey otherwise.</summary>
    public string BadgeClass => Type switch
    {
        "MAJOR" or "DANGEROUS" or "FAIL" => "text-bg-danger",
        "MINOR" or "PRS" => "text-bg-warning",
        _ => "text-bg-secondary"
    };

    /// <summary>A dangerous defect whose type doesn't already say so gets a "Dangerous" badge too.</summary>
    public bool ShowDangerous => Dangerous && Type != "DANGEROUS";
}
