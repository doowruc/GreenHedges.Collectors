# Vehicle Manager

A .NET 10 Razor Pages app for tracking the vehicles I own and their fuel history.

## Running it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd VehicleManager
dotnet run
```

The app needs an Azure SQL database and Auth0 sign-in, so set both up first (see below). Then open
http://localhost:5080. The tables are created on first run, and any pending schema migrations are
applied each time the app starts.

## Database (Azure SQL)

Data is stored in an Azure SQL Database (SQL Server). To set one up:

1. In the [Azure portal](https://portal.azure.com), create a **SQL database**. The free offer
   ("Apply free offer" on the create page) is plenty for this app. Create a new server with
   **SQL authentication** and note the admin login and password.
2. On the server's **Networking** page, allow public access and add your client IP address to the
   firewall rules.
3. Store the connection string in user secrets, from the `VehicleManager` folder:

   ```bash
   dotnet user-secrets set "ConnectionStrings:VehicleManager" "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;User ID=<login>;Password=<password>;Encrypt=True;Connection Timeout=60;"
   ```

A free or serverless database pauses when idle. The first request after a pause can take up to a
minute while it resumes; the app retries automatically. Azure backs the database up automatically
and can restore it to an earlier point in time.

Every table has audit columns: `AddedDate`, `AddedBy`, `ModifiedDate` and `ModifiedBy` (UTC times
and Auth0 user ids, set automatically when rows are saved), and a SQL Server `rowversion` column,
`RowVersion`. If you save an edit to something that was changed after you opened it (e.g. in
another tab), you're warned instead of overwriting the other change.

## Signing in (Auth0)

Every page requires signing in with Auth0. In the Auth0 dashboard, create a **Regular Web
Application** and set:

| Setting | Value |
|---|---|
| Allowed Callback URLs | `https://localhost:7080/callback, http://localhost:5080/callback` |
| Allowed Logout URLs | `https://localhost:7080/Account/SignedOut, http://localhost:5080/Account/SignedOut` |

Then store its settings in user secrets, from the `VehicleManager` folder:

```bash
dotnet user-secrets set "Auth0:Domain" "<your-tenant>.auth0.com"
dotnet user-secrets set "Auth0:ClientId" "<client id>"
dotnet user-secrets set "Auth0:ClientSecret" "<client secret>"
dotnet user-secrets set "AllowedEmails" "me@example.com; someone@example.com"
```

`AllowedEmails` lists who may use the app (separate addresses with `;` or `,`; case doesn't
matter). Signing in isn't enough: the account's email must be on the list **and verified** in
Auth0, otherwise you see an "Access denied" page. Changes to the list apply on the next request.
If you create a user in the Auth0 dashboard, make sure its email ends up verified (e.g. via the
verification email).

Each signed-in user has their own owners, vehicles and fill-ups, and can't see anyone else's.

**Data from before per-user data was added** belongs to nobody until it's claimed. Name who
should get it, then sign in as that person (sign out first if you're already signed in):

```bash
dotnet user-secrets set "ExistingDataOwner" "me@example.com"
```

On that user's next sign-in (with that email verified and on the allow-list) all unclaimed
owners and vehicles, with their fill-ups, become theirs. After that the setting does nothing and
can be removed.

User secrets are only read in Development. Elsewhere, use environment variables
(`ConnectionStrings__VehicleManager`, `Auth0__Domain`, `Auth0__ClientId`, `Auth0__ClientSecret`,
`AllowedEmails`, `ExistingDataOwner`). The app won't start
without them.

Auth0 posts the sign-in result back to the app, which needs `Secure` cookies. Chrome, Edge and
Firefox allow those on `http://localhost`, but Safari doesn't. If you use Safari, run the https
profile instead: `dotnet watch -lp https`, then open https://localhost:7080.

As a second layer on top of the allow-list, disable sign-ups (and any social connections you
don't use) for the application in Auth0.

## Features

**Owners** – managed from **Owners** in the navigation bar. Names must be unique, and an owner
can't be deleted while they still own vehicles.

**Vehicles** – owner, type, make, model, registration number, registration date, purchased date.
Registration numbers are normalised to upper case and must be unique (per user). Vehicles that existed before
owners were added are assigned to an "Unassigned" owner; rename it or reassign the vehicles.

**Fuel Log** (per vehicle) – date, fuel brand and station, litres, cost per litre, optional
voucher discount per litre, and trip computer figures (average MPG, trip miles, range, odometer).
The form pre-fills the brand and station from your last fill-up and shows the total cost,
real MPG and odometer change as you type. For a **split fill-up** (topped up at two stations),
click **Add another station** to record each brand and station in order; lists show them as
"Shell + Sainsbury's" / "Meadowbank + Hill Top".

**Fuel Brands** – under **Manage** in the navigation bar (per user): rename, merge duplicates (e.g. a
misspelling) into the right one, or delete unused ones. Each fuel brand has its own **Stations** page
(e.g. Fuel Brands › Shell) to rename, merge or delete that brand's stations. Every station belongs to a
brand, so one location can have several stations (e.g. Riverside for Asda and for Morrisons); a fill-up
records the station, and its brand comes from the station. New brands and stations are added by typing
them on a fill-up; names match ignoring case. Typing a station that only one brand has fills in the
brand. If a station is rebranded, record it as a new station so earlier fill-ups keep the old brand.
Merging a fuel brand moves its stations to the other brand, combining any with the same name.

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
**Fuel Log page** (`/FuelLog`) – all fill-ups, filterable by vehicle and year.
**Light / dark mode** – follows your system setting by default; pick Light, Dark or System from the
theme menu in the navigation bar. The choice is remembered per browser.

## Importing Fuel2.xlsx

1. Add your vehicles first (e.g. the Mercedes, the Yaris and the A-Class).
2. Go to **Import**, upload the spreadsheet and check the preview.
   Each worksheet becomes one vehicle; sheets are auto-matched to vehicles by name where it is
   unambiguous (a sheet called "Mercedes" with two Mercedes cars has to be picked manually).
3. Click **Import**.

The importer reads the header row (`Date, Brand, Location, Litres, Per Litre, [Voucher], Range,
Miles, MPG, Odometer`) and ignores the spreadsheet's calculated columns. A split fill-up is written
with `+` in both columns, in the same order (`Bx + By` / `Lx + Ly`). Re-importing is safe:
fill-ups already present (same vehicle, date and litres) are skipped, so you can re-upload an
updated spreadsheet to pick up just the new rows.

## Backup and restore

Under **Manage**:

- **Backup** downloads all your data (owners, vehicles, fuel brands, stations and fill-ups) as a
  compressed JSON file, `vehicle-manager-backup-<date>.json.gz`. Other users' data isn't included.
- **Restore** replaces **all** your data with a backup's. It accepts the `.json.gz` file or the plain
  `.json` inside it. The file is checked before anything is changed, and the restore runs in one
  transaction, so if it fails your data is left as it was. Restored rows keep their original audit
  dates and "added by" values, and become yours, so a backup can also be restored into another account.

## Project layout

```
VehicleManager/
  Models/        EF Core entities: Owner, Vehicle, FuelEntry, FuelEntryStop, FuelBrand, FuelStation
  ViewModels/    View (output) and input models used by the pages, with the calculated figures
  Data/          AppDbContext (EF Core + SQL Server), DatabaseMigrator, Migrations/
  Services/      FuelStatsService, FuelImportService (reads .xlsx via ClosedXML), FuelStops, BackupFile + BackupService
  Pages/         Dashboard, Owners/*, Vehicles/*, FuelLog/* (log + fill-up pages), Import/*, Backup/* (backup + restore), FuelBrands/* (and each brand's Stations/*),
                 Account/* (sign in/out)
VehicleManager.Tests/
  Unit/, Data/, Pages/   unit, database and in-memory page tests; Infrastructure/ holds the helpers
```

Bootstrap, Bootstrap Icons and Chart.js are loaded from the jsDelivr CDN.

## Tests

```bash
dotnet test
```

`VehicleManager.Tests` has unit tests, database tests and page tests. The page tests run the whole
app in memory, signed in as a test user. The database and page tests use the Azure database from your
user secrets. Each test runs as its own throwaway `test|…` user, and deletes that user's data
afterwards, so your data isn't touched. Without a connection string they're skipped.

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

## Notes

- Dates and currency use the en-GB culture (dd/MM/yyyy, £).
