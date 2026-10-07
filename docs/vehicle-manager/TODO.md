# To do

Ideas for future features and improvements, roughly in order of how useful they'd be.

## New features

- [ ] **MOT, tax, insurance and service reminders.** Add due dates for each vehicle (MOT, VED, insurance renewal,
      next service). Show what's coming up on the dashboard, and email a reminder about 30 days and 7 days before,
      using the existing Coravel and Resend setup.
  - [ ] Optional: look up MOT and tax due dates from the registration number using the DVLA Vehicle Enquiry API and
        the DVSA MOT History API (both free with an API key). MOT history also gives recorded mileages, which could
        be checked against odometer readings.
- [ ] **Running costs other than fuel.** One `Expense` table (category, date, cost, odometer, notes, optional
      attachments) for servicing, tyres, repairs, insurance, tax, parking and tolls. This makes pence per mile a true
      total cost of ownership rather than fuel only. Add it to the backup.
- [ ] **Vehicle lifecycle: sold/disposed date and an "active" flag.** Hide sold vehicles from the dashboard and the
      fill-up vehicle dropdown, but keep their history. Optionally record purchase and sale price to show depreciation.
- [ ] **Partial fill-ups.** A "full tank" flag on `FuelEntry`, so real MPG is worked out from one full fill to the
      next and a partial fill doesn't skew it. Still total miles ÷ total gallons.
- [ ] **Export.** A ClosedXML `.xlsx` export of the fuel log (filtered by vehicle or year), mirroring the import
      format so data can make the round trip.

## Smaller improvements

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

- [ ] **Persistent logging.** Logs only go to the console now, so a failed nightly backup (`ScheduledBackups`) or an
      exception on a page is lost once the terminal closes. Send them somewhere that keeps them: Application
      Insights or Azure Monitor if hosted in Azure, or Serilog writing rolling files locally. Use structured
      properties (user id, vehicle id), never emails, tokens or connection strings.
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
