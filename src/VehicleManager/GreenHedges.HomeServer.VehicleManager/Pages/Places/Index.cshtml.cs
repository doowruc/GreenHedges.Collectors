namespace VehicleManager.Pages.Places;

public class IndexModel(FuelBrandList brands, FuelStationList stations) : FuelPlacePageModel(brands, stations)
{
    public List<FuelPlaceRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await Places.ListAsync();
}
