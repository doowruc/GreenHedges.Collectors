# Vehicle Manager

Personal .NET 10 Razor Pages app for tracking the vehicles I own and their fuel history.
See `README.md` for user-facing features and the fuel spreadsheet import.

## Commands

Run from `VehicleManager/` (the project folder; the solution is `VehicleManager.slnx` at the repo root):

```bash
dotnet watch                 # run with hot reload → http://localhost:5080 (what I normally use)
dotnet watch -lp https       # same over https → https://localhost:7080 (needed for sign-in in Safari)
dotnet build                 # must stay at 0 warnings, 0 errors
dotnet tool restore          # once, restores the pinned dotnet-ef (dotnet-tools.json at repo root)
dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet list package --vulnerable --include-transitive
```

From the repo root:

```bash
dotnet test                  # all tests (VehicleManager.Tests); must all pass
```

## Tests

`VehicleManager.Tests` (xUnit) is the main way to verify changes. I can't sign in to the running app for you,
so test pages through the in-memory host rather than relying on the browser.

- `Unit/` – pure logic, no database (calculations, stats, allow-list, text splitting).
- `Data/` – EF Core against the real Azure database: per-user isolation, audit columns, concurrency, stops, brands/stations.
- `Pages/` – the whole app in memory (`Infrastructure/AppFactory`, a `WebApplicationFactory<Program>`). It loads pages,
  follows links and submits forms through `PageClient`, which sends a form's current values like a browser.
  Auth0 is replaced by a test sign-in, as whichever user the client is for; Auth0 settings and `AllowedEmails` are
  overridden with dummy values via environment variables.
- Database tests use the real database, so they must never touch real data. Derive from `DatabaseTest`: each test
  runs as its own throwaway `test|<guid>` user, which the per-user query filters keep apart, and
  `TestDatabase.DeleteUserDataAsync` removes everything it owns afterwards (it refuses any other user id).
  Mark them `[DatabaseFact]`/`[DatabaseTheory]` so they're skipped when no connection string is configured.
- Never test against real data or run unscoped `ExecuteUpdate`/`ExecuteDelete` in tests. For a migration, run its
  SQL (`dotnet ef migrations script <from> <to> --no-transactions`) inside a transaction against the real database,
  check the results, and roll back. Then ask me before applying it.
- When fixing a bug, add a test that fails without the fix.

## Stack

- .NET 10, Razor Pages, EF Core 10 + SQL Server (Azure SQL Database). Connection string `ConnectionStrings:VehicleManager` is in user secrets; there is no local database.
- Packages: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all), `ClosedXML`, `Auth0.AspNetCore.Authentication`. Keep EF packages and `dotnet-ef` on the same version.
- Front end: Bootstrap 5.3, Bootstrap Icons and Chart.js 4 from the jsDelivr CDN (no npm/bundler). Small vanilla JS files in `wwwroot/js`.
- en-GB culture throughout (dd/MM/yyyy, £). MPG uses UK gallons (4.54609 L).

## Layout

```
VehicleManager/
  Models/      EF Core entities: Owner, Vehicle, FuelEntry, FuelEntryStop, FuelBrand, FuelStation (storage only),
               AuditedEntity, IUserOwned, IFuelPlace
  ViewModels/  What pages use: *View (output, with calculated figures) and *Input (form/import input)
  Data/        AppDbContext (per-user query filters, audit stamping), DatabaseMigrator, ConcurrencyExtensions,
               DesignTimeDbContextFactory, Migrations/
  Services/    FuelStatsService (summaries), FuelImportService (ClosedXML .xlsx import), FuelCalculations,
               CurrentUser, AllowedUsers, ExistingDataClaim, FuelStops (typed names → stops)
  Pages/       Index (dashboard), Owners/*, Vehicles/*, Fuel/*, Import/*, FuelBrands/* (+ Stations/* per brand),
               Account/* (Login, Logout, SignedOut), Shared/ (layout + partials)
  wwwroot/     css/site.css, js/color-mode.js, js/period-toggle.js
```

## Conventions and how things work

**Database / migrations**
- Schema changes go through EF Core migrations; never use `EnsureCreated()`.
- `Data/DatabaseMigrator.cs` runs at startup (`db.MigrateDatabase(logger)` in `Program.cs`) and applies pending migrations. The migrations were restarted for SQL Server (`InitialCreate`); the SQLite-era ones are gone.
- My real data lives in the Azure SQL database: never drop, recreate or bulk-change it. Tests that write must use a separate database, or rows owned by a fake `test|…` user id that they delete afterwards.
- `EnableRetryOnFailure` is on, so explicit transactions must run inside `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` (see `ExistingDataClaim`).

**Audit columns** (every table, via `Models/AuditedEntity.cs`)
- `AddedDate`, `AddedBy`, `ModifiedDate`, `ModifiedBy` are set in `AppDbContext.SaveChanges` (UTC; user = Auth0 id). Added values can't be changed after insert. Bulk `ExecuteUpdate` bypasses `SaveChanges`, so set `ModifiedDate`/`ModifiedBy` explicitly there.
- `RowVersion` is a SQL Server `rowversion` (`[Timestamp]`), maintained by the database.
- Edits use optimistic concurrency: `*Input` classes carry `RowVersion` (a hidden field in the form, base64), and edit pages save with `db.TrySaveEditAsync(entity, input.RowVersion)` (`Data/ConcurrencyExtensions.cs`). On a conflict, show the error and put the latest `RowVersion` back in the form.
- A new table should inherit `AuditedEntity`.

**Pages never use entity classes** (to limit what's read from the database and prevent overposting)
- Output: project queries straight to view models, e.g. `.Select(VehicleView.Projection)`, or to a small record or anonymous type when a page needs only a few columns. Don't `Include()` navigations or load entities for display.
- Input: bind `[BindProperty]` to an `*Input` class, never an entity. Ids come from the route, not the form. Create a new entity, or load it with `FindAsync`, then copy the fields across with `input.ApplyTo(entity)`.
- Deletes use `ExecuteDeleteAsync()`. Fill-ups go with their vehicle via the FK cascade.
- Entities keep only the attributes that shape the schema (`[Required]`, `[StringLength]`). Display names and validation ranges belong on the `*Input` classes.
- When adding a field, update the entity, its `*View`/`*Input` projections and `ApplyTo`.

**Per-user data**
- Owners and vehicles implement `IUserOwned` (`UserId` = Auth0 `sub`, via `Services/CurrentUser.cs`). Fill-ups belong to the user who owns their vehicle.
- `AppDbContext` query filters limit every query to the current user, including `ExecuteUpdate`/`ExecuteDelete`. `SaveChanges` stamps new owners/vehicles with the user's id and throws if nobody is signed in. Pages don't filter by user themselves.
- With no signed-in user, queries return nothing (fail closed). Only use `IgnoreQueryFilters()` for deliberate cross-user work, as `ExistingDataClaim` does.
- Use `FirstOrDefaultAsync(x => x.Id == id)` rather than `FindAsync`, so the filters always apply.
- Rows with an empty `UserId` are pre-per-user data. On sign-in (`OnTokenValidated`), `ExistingDataClaim` gives them to the user whose verified email matches the `ExistingDataOwner` setting.
- A new user-owned root entity needs `IUserOwned`, a query filter and per-user unique indexes. A child entity needs a filter through its parent.
- `dotnet ef` uses `DesignTimeDbContextFactory`, so it doesn't need the Auth0 settings (it reads the connection string from user secrets).

**Data rules**
- Registration numbers are normalised to upper case and must be unique per user (`Pages/Vehicles/VehicleRules.cs`).
- Every vehicle has a required owner (`Owners` table, FK with `Restrict`, so owners with vehicles can't be deleted). Owner names are unique per user, case-insensitive (`Pages/Owners/OwnerRules.cs`).
- Calculated figures (total cost, real MPG, dash-vs-real, pence per mile) are computed from raw data, never stored. They are properties on `FuelEntryView`; the cost formulas are shared with `FuelEntryInput` via `Services/FuelCalculations.cs`. Average MPG is weighted: total miles ÷ total gallons, not an average of averages.
- Every `FuelStation` belongs to a `FuelBrand` (required `FuelBrandId`). A fill-up's stops are `FuelEntryStop` rows (ordered by `Sequence`) linking only a station; the brand comes from the station. A split fill-up has one stop per station. `FuelEntryView.Brand`/`.Station` join them with " + " for display. Spreadsheet text "Bx + By" / "Lx + Ly" pairs up by position (`FuelStopInput.FromText`).
- A station's brand is fixed, so a rebrand is a new station and history keeps the old brand. Names are case-insensitive: brands are unique per user, stations unique per brand (one location can have stations for several brands).
- Forms take typed brand + station names. `Services/FuelStops.ValidateAsync` requires both (filling in the brand when only one brand has that station name); `SetStopsAsync` reuses or creates them and updates stops in place (marking the fill-up modified so its RowVersion check applies).
- Brands with stations, and stations with fill-ups, can't be deleted (FK `Restrict`); merge them instead. Merging a brand moves its stations, combining same-named ones.
- Fuel brands are managed at `/FuelBrands` (Manage menu); a brand's stations are a sub-page, `/FuelBrands/{brandId}/Stations/...` (`Pages/FuelBrands/Stations`, base `StationPageModel`, which 404s a station not under the brand in the URL). Links and redirects between station pages must pass `brandId`: ASP.NET doesn't carry a route value over to a different page, and the link comes out empty. `FuelPlaceList<T>` (`Pages/FuelBrands/FuelPlaceList.cs`) holds the database logic shared by both, with `FuelBrandList`/`FuelStationList` subclasses.
- Fuel import de-duplicates on (vehicle, date, litres) so re-importing is safe. ClosedXML reads `CachedValue` so formula cells use Excel's saved results.

**Authentication (Auth0)**
- `Auth0:Domain`, `Auth0:ClientId`, `Auth0:ClientSecret`, `AllowedEmails` and optional `ExistingDataOwner` come from user secrets (env vars outside Development). Never put them in `appsettings*.json` or commit them. The app throws at startup if any are missing.
- Authorization code flow, scopes `openid profile email`.
- Allow-list: `AllowedEmails` (user secrets, `;`/`,` separated) via `Services/AllowedUsers.cs`. The `AllowedUser` policy (also the fallback policy, so it covers every endpoint) requires a signed-in user whose `email_verified` email is on the list. Signed-in users who fail it are forbidden → `Account/AccessDenied`. The app throws at startup if the list is missing (fail closed).
- `[AllowAnonymous]` only on `Account/Login`, `Account/Logout`, `Account/SignedOut`, `Account/AccessDenied`, `Error` and static assets. New pages are protected automatically. The layout shows the nav only when the user passes the `AllowedUser` policy.
- `Account/Login` challenges Auth0 and only accepts local return URLs. `Account/Logout` is POST-only and anonymous (so a denied user can switch account). It signs out of both the cookie and Auth0, returning to `Account/SignedOut`.
- To test locally without real credentials, set `AllowedEmails` and the `Auth0__*` env vars to dummy values (e.g. `samples.auth0.com`). Pages then redirect to Auth0 but you can't get past sign-in. Don't sign in with a real account from automated tests.

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
