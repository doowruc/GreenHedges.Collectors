# Vehicle Manager

A .NET 10 Razor Pages app for tracking the vehicles I own and their fuel history.

## Running it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd VehicleManager
dotnet run
```

Then open http://localhost:5080. The SQLite database (`vehiclemanager.db`) is created automatically
next to the project on first run, and any pending schema migrations are applied each time the app
starts. Back it up by copying that file.

## Features

**Owners** – managed from **Owners** in the navigation bar. Names must be unique, and an owner
can't be deleted while they still own vehicles.

**Vehicles** – owner, type, make, model, registration number, registration date, purchased date.
Registration numbers are normalised to upper case and must be unique. Vehicles that existed before
owners were added are assigned to an "Unassigned" owner; rename it or reassign the vehicles.

**Fuel log** (per vehicle) – date, fuel brand, station location, litres, cost per litre, optional
voucher discount per litre, and trip computer figures (average MPG, trip miles, range, odometer).
The form pre-fills the brand and station from your last fill-up and shows the total cost,
real MPG and odometer change as you type.

**Calculated figures** (not stored, always worked out from the raw data):

| Figure | Formula |
|---|---|
| Total cost | litres × (price per litre − voucher) |
| Real MPG | trip miles ÷ (litres ÷ 4.54609) – UK gallons |
| Dash vs real | trip computer MPG − real MPG (red = dash is optimistic) |
| Pence per mile | total cost ÷ trip miles |
| Average MPG | total miles ÷ total gallons (weighted, not an average of averages) |

**Dashboard** – one card per vehicle with 12-month MPG, spend, odometer and last fill-up.
**Vehicle page** – last-12-months / all-time stats, an MPG and fuel-price chart, and the full log.
**Fuel log page** – all fill-ups, filterable by vehicle and year.
**Light / dark mode** – follows your system setting by default; pick Light, Dark or System from the
theme menu in the navigation bar. The choice is remembered per browser.

## Importing Fuel2.xlsx

1. Add your vehicles first (e.g. the Mercedes, the Yaris and the A-Class).
2. Go to **Import**, upload the spreadsheet and check the preview.
   Each worksheet becomes one vehicle; sheets are auto-matched to vehicles by name where it is
   unambiguous (a sheet called "Mercedes" with two Mercedes cars has to be picked manually).
3. Click **Import**.

The importer reads the header row (`Date, Brand, Location, Litres, Per Litre, [Voucher], Range,
Miles, MPG, Odometer`) and ignores the spreadsheet's calculated columns. Re-importing is safe:
fill-ups already present (same vehicle, date and litres) are skipped, so you can re-upload an
updated spreadsheet to pick up just the new rows.

## Project layout

```
VehicleManager/
  Models/        EF Core entities: Owner, Vehicle, FuelEntry
  ViewModels/    View (output) and input models used by the pages, with the calculated figures
  Data/          AppDbContext (EF Core + SQLite), DatabaseMigrator, Migrations/
  Services/      FuelStatsService, FuelImportService (reads .xlsx via ClosedXML)
  Pages/         Dashboard, Owners/*, Vehicles/*, Fuel/*, Import/*
```

Bootstrap, Bootstrap Icons and Chart.js are loaded from the jsDelivr CDN.

## Database migrations

The schema is managed with EF Core migrations in `VehicleManager/Data/Migrations`, applied
automatically at startup. The `dotnet-ef` tool is pinned in `dotnet-tools.json`; restore it once with:

```bash
dotnet tool restore
```

After changing a model or `AppDbContext`, add a migration from the `VehicleManager` folder:

```bash
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

Review the generated file, then run the app to apply it (or `dotnet ef database update`).
To undo the last migration before it has been applied anywhere, use `dotnet ef migrations remove`.

Databases created before migrations were added (via `EnsureCreated()`) are adopted automatically:
on first start the initial migration is recorded as already applied, and no data is touched.

## Notes

- Dates and currency use the en-GB culture (dd/MM/yyyy, £).
