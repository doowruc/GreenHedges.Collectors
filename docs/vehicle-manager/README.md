# Vehicle Manager

A .NET 10 Razor Pages app for tracking the vehicles I own and their fuel history.

## Running it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd VehicleManager
dotnet run
```

Then open http://localhost:5080. The SQLite database (`vehiclemanager.db`) is created automatically
next to the project on first run. Back it up by copying that file.

## Features

**Vehicles** – type, make, model, registration number, registration date, purchased date.
Registration numbers are normalised to upper case and must be unique.

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
  Models/        Vehicle, FuelEntry (with calculated properties)
  Data/          AppDbContext (EF Core + SQLite)
  Services/      FuelStatsService, FuelImportService (reads .xlsx via ClosedXML)
  Pages/         Dashboard, Vehicles/*, Fuel/*, Import/*
```

Bootstrap, Bootstrap Icons and Chart.js are loaded from the jsDelivr CDN.

## Notes

- The database schema is created with `EnsureCreated()`. If you later want EF Core migrations,
  add `Microsoft.EntityFrameworkCore.Design`, run `dotnet ef migrations add InitialCreate`,
  swap `EnsureCreated()` for `Migrate()` in `Program.cs`, and start from a fresh database
  (or re-import).
- Dates and currency use the en-GB culture (dd/MM/yyyy, £).
