using System.Text.Json;
using AwesomeAssertions;
using NEM.CLI.Application;
using NEM.CLI.Scenarios;
using NEM.Contracts;

namespace NEM.CLI.Tests.Scenarios;

public sealed class DataCentreScenarioTests
{
    [Fact]
    public void Run_ExpandsPositiveNameplateAsNamedFlatDemandComponent()
    {
        using var fixture = new ScenarioFixture();
        SystemDispatchResultsDTO baseline = fixture.Run("baseline.json");
        SystemDispatchResultsDTO withDataCentre = fixture.Run("data-centre.json");

        withDataCentre.DataSeries.Demand.AdditiveComponentsByNameMw.Should()
            .ContainKey("Data centre");
        double[] component = withDataCentre.DataSeries.Demand
            .AdditiveComponentsByNameMw["Data centre"];
        component.Should().HaveCount(8_760);
        component.Should().OnlyContain(value => value == 1_000);
        withDataCentre.DataSeries.Demand.TotalDemandMw.Should().OnlyContain(value => value == 1_010);
        (withDataCentre.Metrics.DemandMwh - baseline.Metrics.DemandMwh)
            .Should().Be(8_760_000);
    }

    [Fact]
    public void Run_WithoutNameplateEmitsNoDemandComponent()
    {
        using var fixture = new ScenarioFixture();

        SystemDispatchResultsDTO result = fixture.Run("baseline.json");

        result.DataSeries.Demand.AdditiveComponentsByNameMw.Should().BeEmpty();
        result.DataSeries.Demand.TotalDemandMw.Should().OnlyContain(value => value == 10);
    }

    private sealed class ScenarioFixture : IDisposable
    {
        private const int HoursPerYear = 8_760;

        public ScenarioFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsim-data-centre-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(RootPath, "scenarios"));
            File.WriteAllText(Path.Combine(RootPath, "NemSim.slnx"), string.Empty);
            DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));
            double[] zeroes = new double[HoursPerYear];
            File.WriteAllText(Path.Combine(RootPath, "demand.json"), JsonSerializer.Serialize(
                new ModelInputOutputDTO(
                    2,
                    new Scenario("test", "NSW1", start, start.AddYears(1), TimeSpan.FromHours(1), "hourly"),
                    start.ToUniversalTime(),
                    new Sources(["source.zip"]),
                    new Series(Enumerable.Repeat(10d, HoursPerYear).ToArray()))));
            File.WriteAllText(Path.Combine(RootPath, "weather.json"), JsonSerializer.Serialize(
                new WeatherDataDTO(
                    6,
                    "NSW1",
                    start,
                    TimeSpan.FromHours(1),
                    new SolarWeatherData(
                        "solar.epw",
                        new WeatherLocation("Test", "00000", -33.9, 151.2),
                        zeroes,
                        zeroes,
                        zeroes,
                        Enumerable.Repeat(90d, HoursPerYear).ToArray(),
                        Enumerable.Repeat(20d, HoursPerYear).ToArray(),
                        zeroes),
                    new WindWeatherData(
                        "wind.epw",
                        new WeatherLocation("Test", "00000", -33.9, 151.2),
                        Enumerable.Repeat(5d, HoursPerYear).ToArray(),
                        10,
                        zeroes))));
            WriteScenario("baseline.json", string.Empty);
            WriteScenario("data-centre.json", ", \"dataCentreNameplateMw\": 1000");
        }

        public string RootPath { get; }

        public SystemDispatchResultsDTO Run(string scenarioFile)
        {
            NEM.CLI.Infrastructure.RepositoryPaths paths =
                NEM.CLI.Infrastructure.RepositoryPaths.Discover(RootPath);
            var context = new CliContext(paths, RootPath, TextWriter.Null);

            ScenarioCommand.Run(context, $"scenarios/{scenarioFile}").Should().Be(0);
            return JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
                File.ReadAllBytes(paths.DispatchResultsPath),
                NEM.CLI.Infrastructure.JsonFile.ReadOptions)
                ?? throw new InvalidOperationException("Scenario command produced an empty result.");
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);

        private void WriteScenario(string fileName, string dataCentreProperty) => File.WriteAllText(
            Path.Combine(RootPath, "scenarios", fileName), $$"""
            { "schemaVersion": 4, "id": "{{Path.GetFileNameWithoutExtension(fileName)}}", "name": "Test", "costBasis": { "year": 2026, "realDiscountRate": 0.07 }, "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 }, "regions": [{ "regionId": "NSW1", "demandFile": "demand.json", "weatherFile": "weather.json"{{dataCentreProperty}}, "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 2000, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }] }
            """);
    }
}