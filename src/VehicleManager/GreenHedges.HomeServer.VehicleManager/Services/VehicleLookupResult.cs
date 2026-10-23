namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// What a lookup found. Dates it didn't find (or wasn't asked for) are null; <see cref="Problems"/> says why, for showing to the user.
/// <see cref="MotTests"/> is the DVSA's MOT history (empty for a vehicle too new for an MOT), or null if the DVSA wasn't asked or didn't answer.
/// <see cref="CheckedWith"/> names the services asked, e.g. "the DVSA".
/// </summary>
public sealed record VehicleLookupResult(DateOnly? MotDueDate, DateOnly? TaxDueDate, bool IsSorn, IReadOnlyList<string> Problems,
    IReadOnlyList<MotHistoryTest>? MotTests = null, LookupParts Checked = LookupParts.All, string CheckedWith = "")
{
    /// <summary>E.g. "MOT due 06/11/2026, tax due 01/03/2027".</summary>
    public string Summary
    {
        get
        {
            var found = new List<string>();
            if (MotDueDate is { } mot) found.Add($"MOT due {mot:dd/MM/yyyy}");
            if (TaxDueDate is { } tax) found.Add($"tax due {tax:dd/MM/yyyy}");
            if (IsSorn) found.Add("declared off the road (SORN), so no tax is due");
            if (found.Count > 0) return string.Join(", ", found);
            return Checked switch
            {
                LookupParts.Mot => "no MOT date found",
                LookupParts.Tax => "no tax date found",
                _ => "no MOT or tax dates found"
            };
        }
    }
}
