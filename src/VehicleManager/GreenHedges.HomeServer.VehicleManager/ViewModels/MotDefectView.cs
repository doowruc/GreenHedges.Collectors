namespace VehicleManager.ViewModels;

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
