// Runs the app locally alongside the Aspire dashboard (`aspire run` from the repo root), which shows its logs,
// traces and metrics. Aspire passes the app the OTEL_* settings that point it at the dashboard; the app uses its own
// launch profile (port 5080) and user secrets as with `dotnet watch`. Nothing here is used outside local development.
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.VehicleManager>("vehiclemanager");

builder.Build().Run();
