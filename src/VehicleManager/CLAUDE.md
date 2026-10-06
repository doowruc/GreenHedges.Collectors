# Vehicle Manager

Personal .NET 10 Razor Pages app for tracking the vehicles I own and their fuel history.
See `README.md` for user-facing features and the fuel spreadsheet import.

## Commands

Run from `VehicleManager/` (the project folder; the solution is `VehicleManager.slnx` at the repo root):

```bash
dotnet watch                 # run with hot reload → http://localhost:5080 (what I normally use)
dotnet build                 # must stay at 0 warnings, 0 errors
dotnet tool restore          # once, restores the pinned dotnet-ef (dotnet-tools.json at repo root)
dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet list package --vulnerable --include-transitive
```

There is no test project yet. Verify changes by building and exercising the pages in the running app.

## Stack

- .NET 10, Razor Pages, EF Core 10 + SQLite (`VehicleManager/vehiclemanager.db`, git-ignored with its `-shm`/`-wal` files).
- Packages: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all), `ClosedXML`. Keep EF packages and `dotnet-ef` on the same version.
- Front end: Bootstrap 5.3, Bootstrap Icons and Chart.js 4 from the jsDelivr CDN (no npm/bundler). Small vanilla JS files in `wwwroot/js`.
- en-GB culture throughout (dd/MM/yyyy, £). MPG uses UK gallons (4.54609 L).

## Layout

```
VehicleManager/
  Models/      Owner, Vehicle, FuelEntry (calculated figures are properties, not stored)
  Data/        AppDbContext, DatabaseMigrator, Migrations/
  Services/    FuelStatsService (summaries), FuelImportService (ClosedXML .xlsx import)
  Pages/       Index (dashboard), Owners/*, Vehicles/*, Fuel/*, Import/*, Shared/ (layout + partials)
  wwwroot/     css/site.css, js/color-mode.js, js/period-toggle.js
```

## Conventions and how things work

**Database / migrations**
- Schema changes go through EF Core migrations; never use `EnsureCreated()`.
- `Data/DatabaseMigrator.cs` runs at startup (`db.MigrateDatabase(logger)` in `Program.cs`) and applies pending migrations. It also adopts old databases created by `EnsureCreated()` (tables present, no `__EFMigrationsHistory`) by recording `InitialCreate` as applied. Don't remove that while such databases might exist.
- My real data lives in the local `vehiclemanager.db`: don't delete or recreate it. Test against a copy.

**Data rules**
- Registration numbers are normalised to upper case and must be unique (`Pages/Vehicles/VehicleRules.cs`).
- Every vehicle has a required owner (`Owners` table, FK with `Restrict`, so owners with vehicles can't be deleted). Owner names are unique, case-insensitive (`Pages/Owners/OwnerRules.cs`).
- Calculated figures (total cost, real MPG, dash-vs-real, pence per mile) are computed from raw data, never stored. Average MPG is weighted: total miles ÷ total gallons, not an average of averages.
- Fuel import de-duplicates on (vehicle, date, litres) so re-importing is safe. ClosedXML reads `CachedValue` so formula cells use Excel's saved results.

**Routing**: path ids, e.g. `/Vehicles/Details/1`, `/Fuel/Edit/5`.

**Light / dark mode**
- Bootstrap's `data-bs-theme` on `<html>`, set by `wwwroot/js/color-mode.js`. It's loaded synchronously in `<head>` so there's no flash.
- Preference (Light / Dark / System) is stored in localStorage key `theme`; absent means System, the default. The theme menu is the rightmost item in the navbar. The navbar stays dark in both themes.
- Everything must work in both themes. Don't use `bg-white`, `table-light` or hard-coded colours. Use `bg-body`, the custom `<thead class="table-head">` and Bootstrap CSS variables in `site.css`.
- Anything drawing its own colours (e.g. Chart.js) should listen for the `themechange` event and use `window.themeColors()`. See `Pages/Vehicles/Details.cshtml`.

**Stats period toggle ("Last 12 months" / "All time")**
- Shared partial `Pages/Shared/_PeriodToggle.cshtml` plus `wwwroot/js/period-toggle.js`, used on the dashboard and vehicle details. Reuse it rather than duplicating it.
- Mark up period-specific content as `<div class="period-stats" data-period="12m">` / `data-period="all"`. A `site.css` rule hides the non-selected one, based on `data-stats-period` on `<html>`.
- The selection is remembered across pages in the `statsPeriod` cookie (`all`; absent means 12 months). The partial renders the active button server-side from the cookie, and the script runs synchronously right after the buttons, so nothing flickers on load. Keep it that way.

**General**
- Prefer shared partials and small shared scripts over copying markup or JS between pages.
- Avoid visible flicker on page load: render initial state server-side where possible.
- Check pages at phone width as well as desktop.

## Git

- This repo's local git config sets the identity to `doowruc <8654129+doowruc@users.noreply.github.com>`. Commits must use it.
- Only commit or push when I ask.
