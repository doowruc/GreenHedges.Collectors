# To do

Ideas for future features and improvements, roughly in order of how useful they'd be.

## New features

- [x] **MOT, tax, insurance and service reminders.** Add due dates for each vehicle (MOT, VED, insurance renewal,
      next service). Show what's coming up on the dashboard, and email a reminder about 30 days and 7 days before,
      using the existing Coravel and Resend setup.
  - [x] Optional: look up MOT and tax due dates from the registration number using the DVLA Vehicle Enquiry API and
        the DVSA MOT History API (both free with an API key). Each is optional; messages name only the ones set up.
    - [x] Register for the DVSA MOT History API and add its four `MotHistory:*` settings.
    - [ ] Register for the DVLA Vehicle Enquiry Service once it reopens (registration was closed in October 2026)
          and add `Dvla:ApiKey`, so tax dates are looked up too.
  - [x] MOT history on the vehicle page: an MOT tab listing every test with its defects and advisories (kept in step
        with the DVSA on each check, and in the backup), a mileage chart whose points scroll to their test, and
        "Previous owner" badges for tests before the purchase date. Header links check the MOT and tax separately.
  - [x] Use the MOT tests' mileages in the mileage trend, alongside the fill-ups'.
  - [ ] Check the MOT tests' recorded mileages against the fill-up odometer readings, and flag big mismatches.
  - [x] Service due by mileage as well as date, estimated from the recent miles per month.
- [x] **Service history** for each vehicle: date, mileage, garage and location, and whether it was under a previous
      owner (ticked automatically for dates before the purchase date). A service interval (months and/or miles) on the
      vehicle sets the next service's due date and mileage when its latest service is added. The history is a
      DataTable with red badges for services later than the interval after the one before.
- [ ] **Running costs other than fuel.** One `Expense` table (category, date, cost, odometer, notes, optional
      attachments) for servicing, tyres, repairs, insurance, tax, parking and tolls. This makes pence per mile a true
      total cost of ownership rather than fuel only. Add it to the backup.
  - [ ] Start with a cost on each service record, now that there's a service history.
- [ ] **Vehicle lifecycle: sold/disposed date and an "active" flag.** Hide sold vehicles from the dashboard and the
      fill-up vehicle dropdown, but keep their history. Optionally record purchase and sale price to show depreciation.
- [ ] **Partial fill-ups.** A "full tank" flag on `FuelEntry`, so real MPG is worked out from one full fill to the
      next and a partial fill doesn't skew it. Still total miles ÷ total gallons.
- [ ] **Export.** A ClosedXML `.xlsx` export of the fuel log (filtered by vehicle or year), mirroring the import
      format so data can make the round trip.

## Smaller improvements

- [x] **Vehicle page tabs:** Fuel, Services and MOT on separate tabs, the active one kept in the address (`?tab=`) and
      when switching vehicle. Services and MOT have the 12 months / all time switch and a mileage chart, with previous
      owners' readings shown differently.
- [x] **Tidier menus and buttons:** the theme choice is a Theme item in the user menu; Add fill-up moved from the menu
      bar onto the Fuel Log page, the Fuel tab and the dashboard cards (with Add service beside it); small buttons,
      selects and pills (period toggle, station fuel types) throughout.
- [x] **Back buttons instead of breadcrumbs:** the station list and station page have a Back button in the header (the
      station page's returns to wherever it was opened from); form pages rely on Cancel. Every form's Cancel and Back
      is an outlined button rather than a link.
- [ ] **Odometer sanity checks on the fill-up form:** warn (not error) when the odometer is lower than the previous
      fill-up's, or when trip miles and the odometer difference disagree a lot.
- [ ] **Prefill the fill-up form** with the last vehicle, station and pump price used, and today's date.
- [ ] **Station comparison:** a table of the cheapest stations over the last 3 months for each fuel type, building on
      the station pages' price data.
- [ ] **Monthly and annual spend chart** on the dashboard, plus a year-on-year comparison.
- [ ] **Installable on a phone (PWA):** a manifest and icons so it opens like an app from the home screen.
- [ ] **Receipt photo** on a fill-up, stored in Azure Blob Storage. Only if it'd actually be used: it adds storage,
      backup format changes and some cost.

## Technical improvements

- [x] **Edit conflict warnings now show on every edit page:** the vehicle and owner pages hid "someone changed this
      after you opened it", because the validation summary was inside the prefixed form partials. Fixed and tested.
- [x] **One type per file** throughout, now a rule in CLAUDE.md.
- [x] **Shared DataTables setup:** `data-table.js` and `_DataTableStyles`/`_DataTableScripts` serve both the fuel log
      and the service history, so a new table follows the same pattern.
- [ ] **Develop against a local database again,** instead of the Azure one. It must stay SQL Server, because the
      model relies on it (`rowversion` concurrency, the unique index that counts NULLs as equal, `date` columns,
      retry-on-failure). Support all three platforms, with a setup for each:
  - Windows: SQL Server Express LocalDB (`(localdb)\MSSQLLocalDB`), which comes with Visual Studio or as a small
    separate install.
  - Mac and Linux: SQL Server in a container (Docker or Podman, the `mcr.microsoft.com/mssql/server` image; on Apple
    Silicon it runs under Rosetta in Docker Desktop). Not Azure SQL Edge, which has been retired.
  - Choose by the `ConnectionStrings:VehicleManager` user secret, as now. Document each setup in the README, maybe
    with a `compose.yaml` for the container.
  - Database tests would then run locally too (still as throwaway `test|…` users). Use backup and restore to copy
    real data between the Azure and local databases.
- [ ] **Persistent logging, with OpenTelemetry.** Logs only went to the console, so a failed nightly job
      (`ScheduledBackups`, `ScheduledReminders`) or a page exception was lost once the terminal closed. OpenTelemetry
      (OTel) logs, traces and metrics are sent over OTLP, so the destination can change without changing code.
  - [x] Locally, with the Aspire dashboard: `VehicleManager.AppHost` runs the app alongside the dashboard (no Docker
        needed). The app sends telemetry only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, as the AppHost does; plain
        `dotnet watch` and the tests stay console-only. Logs go through `builder.Logging.AddOpenTelemetry`, so existing
        `ILogger` calls are kept; traces cover ASP.NET Core, `HttpClient` (Resend, DVLA, DVSA) and SQL Client; each
        nightly job run is a trace with a span per user. Logs no longer carry registrations or emails, and the DVSA's
        request URLs are redacted in traces.
  - [ ] Grafana Cloud, for telemetry kept beyond a local session: [the free tier](https://grafana.com/products/cloud/free-tier)
        (50 GB each of logs and traces a month, 10k metric series, 14 days' retention, free indefinitely) takes OTLP
        directly. Set the standard `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS` (the stack's token) and
        `OTEL_SERVICE_NAME` from user secrets or environment variables, as with the other secrets; the endpoint and
        token are on the stack's OpenTelemetry setup page. No code changes needed. Grafana's alerting could then cover
        "Alert on failures" below, e.g. a failed nightly job or no successful backup in 48 hours. It sends data to a
        third party, so check what the logs and traces carry first: user ids are Auth0 ids; Resend errors may quote
        an email address.
- [ ] **Log more of what matters.** Sign-ins and access-denied attempts (`AllowedUsers`), restores and imports
      (counts of rows), merges of brands/stations, concurrency conflicts in `TrySaveEditAsync`, and unhandled
      exceptions via the `/Error` page with its request id.
- [ ] **Alert on failures.** Email (via Resend) or alert when the nightly backup job fails for any user, instead of
      only logging it. Optionally a weekly "all OK" summary so silence isn't mistaken for success.
- [ ] **Health-check endpoint** (`AddHealthChecks` with a database check), if the app is ever hosted somewhere other
      than locally.
- [ ] **CI on GitHub Actions:** build (0 warnings) and run the unit and page tests on each push. Database tests skip
      without a connection string, so they can stay local.
- [ ] **Dependency checks:** Dependabot for NuGet updates, or run `dotnet list package --vulnerable
      --include-transitive` and check for outdated packages from time to time.
- [ ] **Security headers:** a Content-Security-Policy allowing only self and the jsDelivr CDN (with Subresource
      Integrity hashes on the CDN scripts and stylesheets), plus `X-Content-Type-Options` and `Referrer-Policy`.
- [ ] **Rate limiting** on the upload pages (import and restore) and a maximum request size, using ASP.NET's
      built-in rate limiter.
- [ ] **Deployment**, if it's ever hosted: Azure App Service with settings in Key Vault or app settings, a managed
      identity for Azure SQL instead of a SQL password, and applying migrations as a deploy step rather than at
      startup.
