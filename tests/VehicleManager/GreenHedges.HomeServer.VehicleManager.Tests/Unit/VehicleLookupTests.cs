using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VehicleManager.Data;
using VehicleManager.Models;
using VehicleManager.Services;
using VehicleManager.Tests.Infrastructure;

namespace VehicleManager.Tests.Unit;

public class VehicleLookupTests
{
    private static readonly DvlaOptions Dvla = new(FakeVehicleApis.ApiKey, DvlaOptions.DefaultBaseUrl);
    private static readonly MotHistoryOptions Dvsa = new("tests", "tests", FakeVehicleApis.ApiKey, FakeVehicleApis.TokenUrl,
        MotHistoryOptions.DefaultScope, MotHistoryOptions.DefaultBaseUrl);
    private static readonly DvlaOptions NoDvla = new(null, DvlaOptions.DefaultBaseUrl);
    private static readonly MotHistoryOptions NoDvsa = new(null, null, null, null, MotHistoryOptions.DefaultScope, MotHistoryOptions.DefaultBaseUrl);

    private static VehicleLookup Lookup(DvlaOptions? dvla = null, MotHistoryOptions? dvsa = null,
        MotHistoryTokenCache? tokens = null, FakeVehicleApis? apis = null, ILogger<VehicleLookup>? logger = null)
    {
        dvla ??= Dvla;
        dvsa ??= Dvsa;
        apis ??= new FakeVehicleApis();
        return new VehicleLookup(NoDatabase(),
            new DvlaClient(new HttpClient(apis, disposeHandler: false) { BaseAddress = new Uri(dvla.BaseUrl) }, dvla), dvla,
            new MotHistoryClient(new HttpClient(apis, disposeHandler: false) { BaseAddress = new Uri(dvsa.BaseUrl) }, dvsa, tokens ?? new()), dvsa,
            logger ?? NullLogger<VehicleLookup>.Instance);
    }

    /// <summary>A context that's never connected: refreshing a vehicle that isn't saved doesn't look in the database.</summary>
    private static AppDbContext NoDatabase() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused.invalid").Options);

    [Fact]
    public async Task Tax_comes_from_the_DVLA_and_the_MOT_from_the_latest_DVSA_test()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle
        {
            TaxDueDate = new(2027, 3, 1), DvlaMotExpiryDate = new(2026, 11, 1),
            MotTestExpiryDates = [new(2025, 11, 3), new(2026, 11, 2)]
        });

        var result = await Lookup().LookUpAsync(reg);
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2027, 3, 1)), (result.MotDueDate, result.TaxDueDate));
        Assert.Empty(result.Problems);
        Assert.Equal("MOT due 02/11/2026, tax due 01/03/2027", result.Summary);
    }

    [Fact]
    public async Task A_new_vehicles_MOT_is_its_first_one()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { TaxDueDate = new(2027, 3, 1), FirstMotDueDate = new(2028, 9, 30) });
        Assert.Equal(new DateOnly(2028, 9, 30), (await Lookup().LookUpAsync(reg)).MotDueDate);
    }

    [Fact]
    public async Task Without_the_DVSA_the_MOT_comes_from_the_DVLA()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { DvlaMotExpiryDate = new(2026, 11, 1), MotTestExpiryDates = [new(2026, 12, 1)] });
        Assert.Equal(new DateOnly(2026, 11, 1), (await Lookup(dvsa: NoDvsa).LookUpAsync(reg)).MotDueDate);
    }

    [Fact]
    public async Task One_service_failing_doesnt_stop_the_other()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { DvlaFails = true, MotTestExpiryDates = [new(2026, 12, 1)] });
        var result = await Lookup().LookUpAsync(reg);
        Assert.Equal((new DateOnly(2026, 12, 1), (DateOnly?)null), (result.MotDueDate, result.TaxDueDate));
        Assert.Equal("The DVLA returned an error (500: System error occurred).", Assert.Single(result.Problems));
    }

    [Fact]
    public async Task Failures_are_logged_without_the_registration()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { DvlaFails = true, MotTestExpiryDates = [new(2026, 12, 1)] });
        var logger = new ListLogger<VehicleLookup>();
        await Lookup(logger: logger).LookUpAsync(reg);

        var logged = Assert.Single(logger.Messages);
        Assert.Contains("DVLA lookup failed", logged);
        Assert.DoesNotContain(reg, logged);
        Assert.DoesNotContain(VehicleLookup.Compact(reg), logged);
    }

    [Fact]
    public async Task An_unknown_registration_is_reported()
    {
        var reg = FakeVehicleApis.NewRegistration();
        var result = await Lookup().LookUpAsync(reg);
        Assert.Equal([$"The DVLA has no vehicle registered {reg}.", $"The DVSA has no MOT record for {reg}."], result.Problems);
        Assert.Equal("no MOT or tax dates found", result.Summary);
    }

    [Fact]
    public async Task Refreshing_keeps_dates_that_werent_found_but_SORN_clears_the_tax()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { TaxStatus = "SORN", TaxDueDate = new(2026, 1, 1), AtDvsa = false });
        var vehicle = new Vehicle { RegistrationNumber = reg, MotDueDate = new(2026, 12, 25), TaxDueDate = new(2026, 12, 1) };

        var result = await Lookup().RefreshAsync(vehicle);
        Assert.True(result.IsSorn);
        Assert.Equal((new DateOnly(2026, 12, 25), (DateOnly?)null), (vehicle.MotDueDate, vehicle.TaxDueDate));
        Assert.Contains("SORN", result.Summary);
    }

    [Fact]
    public async Task The_DVSA_sends_the_MOT_history()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { MotTestExpiryDates = [new(2025, 11, 3), new(2026, 11, 2)] });
        Assert.Equal(3, (await Lookup().LookUpAsync(reg)).MotTests?.Count);   // two passes and a failure

        // None, rather than an empty history, when the DVSA doesn't know the vehicle or isn't asked.
        Assert.Null((await Lookup().LookUpAsync(FakeVehicleApis.NewRegistration())).MotTests);
        Assert.Null((await Lookup(dvsa: NoDvsa).LookUpAsync(reg)).MotTests);
    }

    [Fact]
    public async Task Refreshing_brings_in_the_MOT_tests()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { MotTestExpiryDates = [new(2026, 11, 2)] });
        var vehicle = new Vehicle { RegistrationNumber = reg };

        await Lookup().RefreshAsync(vehicle);
        Assert.Equal([true, false], vehicle.MotTests.Select(t => t.Passed));
        Assert.Equal(FakeVehicleApis.FailedTestDefect, vehicle.MotTests[1].Defects[0].Text);
    }

    [Fact]
    public async Task Checking_just_the_MOT_asks_only_the_DVSA()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { DvlaFails = true, TaxDueDate = new(2027, 3, 1), MotTestExpiryDates = [new(2026, 11, 2)] });
        var result = await Lookup().LookUpAsync(reg, LookupParts.Mot);
        Assert.Equal((new DateOnly(2026, 11, 2), (DateOnly?)null, "the DVSA"), (result.MotDueDate, result.TaxDueDate, result.CheckedWith));
        Assert.Empty(result.Problems);   // the failing DVLA wasn't asked

        // With only the DVLA, it has the MOT date.
        var dvlaReg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { TaxDueDate = new(2027, 3, 1), DvlaMotExpiryDate = new(2026, 12, 1) });
        result = await Lookup(dvsa: NoDvsa).LookUpAsync(dvlaReg, LookupParts.Mot);
        Assert.Equal((new DateOnly(2026, 12, 1), (DateOnly?)null, "the DVLA"), (result.MotDueDate, result.TaxDueDate, result.CheckedWith));
    }

    [Fact]
    public async Task Checking_just_the_tax_leaves_the_MOT_alone()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { TaxDueDate = new(2027, 3, 1), DvlaMotExpiryDate = new(2026, 12, 1), MotTestExpiryDates = [new(2026, 11, 2)] });
        var vehicle = new Vehicle { RegistrationNumber = reg, MotDueDate = new(2026, 1, 1) };

        var result = await Lookup().RefreshAsync(vehicle, LookupParts.Tax);
        Assert.Equal((new DateOnly(2026, 1, 1), (DateOnly?)new DateOnly(2027, 3, 1)), (vehicle.MotDueDate, vehicle.TaxDueDate));
        Assert.Empty(vehicle.MotTests);
        Assert.Equal(("the DVLA", "tax due 01/03/2027"), (result.CheckedWith, result.Summary));
    }

    [Fact]
    public async Task Says_what_wasnt_found()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { AtDvsa = false });
        Assert.Equal("no MOT date found", (await Lookup().LookUpAsync(reg, LookupParts.Mot)).Summary);
        Assert.Equal("no tax date found", (await Lookup().LookUpAsync(reg, LookupParts.Tax)).Summary);
    }

    [Fact]
    public async Task The_DVSA_token_is_reused()
    {
        var reg = FakeVehicleApis.Add(FakeVehicleApis.NewRegistration(), new FakeVehicle { MotTestExpiryDates = [new(2026, 12, 1)] });
        var tokens = new MotHistoryTokenCache();
        var apis = new FakeVehicleApis();
        await Lookup(tokens: tokens, apis: apis).LookUpAsync(reg);
        var result = await Lookup(tokens: tokens, apis: apis).LookUpAsync(reg);
        Assert.Equal(new DateOnly(2026, 12, 1), result.MotDueDate);
        Assert.Equal(1, apis.TokenRequests);
    }

    [Fact]
    public void Lookups_need_at_least_one_service()
    {
        Assert.False(Lookup(NoDvla, NoDvsa).IsAvailable);
        Assert.True(Lookup(Dvla, NoDvsa).IsAvailable);
        Assert.True(Lookup(NoDvla, Dvsa).IsAvailable);
    }

    [Fact]
    public void Messages_name_only_the_services_set_up()
    {
        var both = Lookup(Dvla, Dvsa);
        Assert.Equal(("the DVLA and DVSA", "MOT and tax", "MOT and tax are checked with the DVLA and DVSA each morning."),
            (both.ServiceNames, both.LookedUp, both.MorningCheck));

        var dvlaOnly = Lookup(Dvla, NoDvsa);
        Assert.Equal(("the DVLA", "MOT and tax", "MOT and tax are checked with the DVLA each morning."),
            (dvlaOnly.ServiceNames, dvlaOnly.LookedUp, dvlaOnly.MorningCheck));

        // The DVSA has no tax dates.
        var dvsaOnly = Lookup(NoDvla, Dvsa);
        Assert.Equal(("the DVSA", "MOT", "MOT is checked with the DVSA each morning."),
            (dvsaOnly.ServiceNames, dvsaOnly.LookedUp, dvsaOnly.MorningCheck));

        Assert.Null(Lookup(NoDvla, NoDvsa).MorningCheck);
    }

    [Fact]
    public void The_DVSA_settings_are_all_or_none()
    {
        static IConfiguration Settings(params (string, string)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Item1, v => (string?)v.Item2)).Build();

        Assert.False(MotHistoryOptions.From(Settings()).IsConfigured);
        Assert.True(MotHistoryOptions.From(Settings(("ClientId", "a"), ("ClientSecret", "b"), ("ApiKey", "c"), ("TokenUrl", "https://t"))).IsConfigured);
        Assert.Throws<InvalidOperationException>(() => MotHistoryOptions.From(Settings(("ClientId", "a"))));
    }

    [Fact]
    public void Registrations_are_sent_without_spaces() => Assert.Equal("AB12CDE", VehicleLookup.Compact("ab12 cde"));
}
