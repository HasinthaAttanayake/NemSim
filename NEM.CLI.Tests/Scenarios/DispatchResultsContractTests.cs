using AwesomeAssertions;
using NEM.CLI.Demand;
using NEM.CLI.Scenarios;
using NEM.Contracts;
using NEM.Model.Economics;
using NEM.Model.Grid;
using NEM.Model.Scenarios;
using NEM.Model.Series;
using NEM.Model.Simulation;
using NEM.Model.StorageSizing;
using NEM.Model.Units;
using System.Text;
using System.Text.Json;
using DomainScenario = NEM.Model.Scenarios.Scenario;

namespace NEM.CLI.Tests.Scenarios;

public sealed class DispatchResultsContractTests
{
    [Fact]
    public void StorageSizingOutcome_StorageNoLongerImprovesReliability_RoundTripsAsAString()
    {
        string json = JsonSerializer.Serialize(StorageSizingOutcome.StorageNoLongerImprovesReliability);

        json.Should().Be("\"storageNoLongerImprovesReliability\"");
        JsonSerializer.Deserialize<StorageSizingOutcome>(json)
            .Should().Be(StorageSizingOutcome.StorageNoLongerImprovesReliability);
    }

    [Fact]
    public void InputArtifact_IdentifiesTheExactParsedBytes()
    {
        byte[] contents = Encoding.UTF8.GetBytes("overwritable input");
        string path = Path.Combine("inputs", "demand-data.json");

        DispatchInputArtifactDTO artifact = ScenarioRunner.CreateArtifact(
            path,
            2,
            contents);

        artifact.Should().Be(new DispatchInputArtifactDTO(
            "demand-data.json",
            2,
            "25010854efed1ed4a47708a74f5c201dc04616acc95d7b3381e641ca0483ccaf"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_PublishesCanonicalPerFleetDeliveredGeneration(bool includesStorage)
    {
        var start = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        double generationSourcedChargeMwh = includesStorage ? 20 : 0;
        FlowSeries baseDemand = Flow(start, 100 - generationSourcedChargeMwh, 100);
        FlowSeries additiveDemand = Flow(start, 20, 10);
        FlowSeries totalDemand = Flow(start, 120 - generationSourcedChargeMwh, 110);
        FlowSeries zero = Flow(start, 0, 0);
        var outcome = new DispatchOutcome(
            "NSW1",
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 140, 50),
                [GenerationTechnology.Gas] = Flow(start, 0, 60),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 20, 0),
                [GenerationTechnology.Gas] = zero,
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, 120 - generationSourcedChargeMwh, 50),
                [GenerationTechnology.Gas] = Flow(start, 0, 60),
            },
            new Dictionary<GenerationTechnology, FlowSeries>
            {
                [GenerationTechnology.Coal] = Flow(start, generationSourcedChargeMwh, 0),
                [GenerationTechnology.Gas] = zero,
            },
            totalDemand,
            zero,
            Flow(start, generationSourcedChargeMwh, 0),
            zero,
            zero,
            zero,
            stateOfChargeByTechnology: includesStorage
                ? new Dictionary<StorageTechnology, StockSeries>
                {
                    [StorageTechnology.Battery] = new StockSeries(
                        start,
                        TimeSpan.FromHours(1),
                        AnnualValues(start, 0, 8.7)),
                }
                : []);
        GeneratingFleet[] fleets =
        [
            new(GenerationTechnology.Coal, Power.FromMegawatts(140)),
            new(GenerationTechnology.Gas, Power.FromMegawatts(60)),
        ];
        var demandData = new OperationalDemandData("NSW1", baseDemand, ["demand.zip"]);
        var scenario = new DomainScenario(
            new ScenarioId("nsw1-baseline-dispatch"),
            "NSW1 baseline dispatch",
            start,
            start.AddYears(1),
            [new ScenarioRegion(
                "NSW1",
                [
                    new ScenarioGeneratingFleet(
                        GenerationTechnology.Coal,
                        Power.FromMegawatts(140),
                        CreateCostParameters(),
                        CreateTechnologyProfile()),
                    new ScenarioGeneratingFleet(
                        GenerationTechnology.Gas,
                        Power.FromMegawatts(60),
                        CreateCostParameters(),
                        CreateTechnologyProfile()),
                ],
                includesStorage
                    ? [new ScenarioStorageFleet(
                        StorageTechnology.Battery,
                        Energy.FromMegawattHours(120),
                        Power.FromMegawatts(30),
                        new StorageCostParameters(
                            PowerCapacityCost.FromAudPerMwCapacity(0),
                            EnergyCapacityCost.FromAudPerMwhCapacity(0),
                            AnnualPowerCapacityCost.FromAudPerMwYear(0)),
                        new StorageTechnologyProfile(15u, 0.87))]
                    : [])],
            new CostBasis(2026, 0.07m));
        var powerSystem = new PowerSystem(
            new PowerSystemId("nsw1-baseline-dispatch-system"),
            scenario.Id,
            [new Region(
                "NSW1",
                fleets,
                baseDemand,
                [new DemandComponent("Data centres", additiveDemand)],
                storageFleets: includesStorage
                    ?
                    [
                        new StorageFleet(
                            StorageTechnology.Battery,
                            Energy.FromMegawattHours(120),
                            Power.FromMegawatts(30),
                            new StorageTechnologyProfile(15u, 0.87),
                            Energy.Zero),
                    ]
                    : [])]);

        var installedCapacity = new RegionalBatterySizing(
            "NSW1",
            Energy.FromMegawattHours(includesStorage ? 120 : 0),
            Power.FromMegawatts(includesStorage ? 30 : 0),
            wasChanged: false);
        var sizingResult = new StorageSizingRunResult(
            powerSystem,
            [new RegionalSizingResult(
                outcome,
                installedCapacity,
                meetsTarget: true,
                StorageSizingStatus.TargetMet,
                "The installed Battery meets the reliability target.")],
            [new InstalledBatteryAssessment(
                outcome,
                installedCapacity,
                meetsTarget: true,
                "The installed Battery meets the reliability target.")],
            dispatchPassCount: 1,
            StorageSizingStatus.TargetMet,
            "The installed Battery meets the reliability target.");

        var weather = new WeatherDataDTO(
            ArtifactSchemaVersions.Weather,
            "NSW1",
            start,
            TimeSpan.FromHours(1),
            new SolarWeatherData(
                "sydney-solar.epw",
                new WeatherLocation("Sydney", "947680", -33.9, 151.2),
                [],
                [],
                [],
                [],
                [],
                []),
            new WindWeatherData(
                "sydney-wind.epw",
                new WeatherLocation("Sydney", "947680", -33.9, 151.2),
                [],
                10,
                []));
        var dispatch = new ScenarioDispatchResult(
            scenario,
            powerSystem,
            sizingResult,
            PowerSystemCostCalculator.Calculate(scenario, powerSystem, [outcome]),
            new Dictionary<string, LoadedInput<OperationalDemandData>>(StringComparer.OrdinalIgnoreCase)
            {
                ["NSW1"] = new LoadedInput<OperationalDemandData>(
                    demandData,
                    new DispatchInputArtifactDTO("demand.json", 2, new string('a', 64))),
            },
            new Dictionary<string, LoadedInput<WeatherDataDTO>>(StringComparer.OrdinalIgnoreCase)
            {
                ["NSW1"] = new LoadedInput<WeatherDataDTO>(
                    weather,
                    new DispatchInputArtifactDTO("weather.json", 5, new string('b', 64))),
            });

        string rootPath = Path.Combine(Path.GetTempPath(), $"nemsim-dispatch-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        try
        {
            string resultsPath = Path.Combine(rootPath, "results.json");
            DispatchResultsExport.WritePublication(
                new DispatchPublicationRequest(
                    dispatch,
                    new StorageSizingOptions(
                        Power.FromMegawatts(10_000),
                        Energy.FromMegawattHours(100_000)),
                    "NEM reliability standard"),
                resultsPath);
            RegionDispatchResultsDTO result = JsonSerializer.Deserialize<RegionDispatchResultsDTO>(
                File.ReadAllBytes(Path.Combine(rootPath, "results-nsw1.json")),
                NEM.CLI.Infrastructure.JsonFile.ReadOptions)!;

            result.SchemaVersion.Should().Be(ArtifactSchemaVersions.RegionDispatchResults);
            result.DataSeries.DeliveredGenerationByTechnologyMw["Coal"].Take(2)
                .Should().Equal(120 - generationSourcedChargeMwh, 50);
            result.DataSeries.DeliveredGenerationByTechnologyMw["Gas"].Take(2)
                .Should().Equal(0, 60);
            result.DataSeries.DeliveredGenerationByTechnologyMw.Keys.Should().Equal("Coal", "Gas");
            result.DataSeries.DeliveredGenerationByTechnologyMw["Coal"].Sum()
                .Should().Be(
                    outcome.PerFleetGeneration[GenerationTechnology.Coal]
                        .Subtract(outcome.PerFleetCurtailment[GenerationTechnology.Coal])
                        .Integrate().MegawattHours - generationSourcedChargeMwh);
            result.DataSeries.DeliveredGenerationByTechnologyMw.Values
                .SelectMany(series => series)
                .Sum()
                .Should().Be(230 - generationSourcedChargeMwh);
            result.DataSeries.Demand.BaseDemandMw!.Take(2)
                .Should().Equal(100 - generationSourcedChargeMwh, 100);
            result.DataSeries.Demand.AdditiveComponentsByNameMw["Data centres"].Take(2)
                .Should().Equal(20, 10);
            result.DataSeries.Demand.TotalDemandMw.Take(2)
                .Should().Equal(120 - generationSourcedChargeMwh, 110);
            if (includesStorage)
            {
                result.PowerSystem.StorageFleets.Should().ContainSingle().Which
                    .Should().Be(new DispatchStorageFleetDTO("Battery", 120, 30));
                result.DataSeries.StateOfChargeByTechnologyMwh["Battery"].Take(2)
                    .Should().Equal(0, 8.7);
            }
            else
            {
                result.PowerSystem.StorageFleets.Should().BeEmpty();
                result.DataSeries.StateOfChargeByTechnologyMwh.Should().BeEmpty();
            }
            result.Metrics.Should().Be(new DispatchMetricsDTO(
                230 - generationSourcedChargeMwh,
                230 - generationSourcedChargeMwh,
                20,
                0,
                0,
                0,
                8760.0 / 8760,
                0,
                new IntervalPointersDTO(null, 0, includesStorage ? 0 : null)));
            result.Reliability.Should().Be(new ReliabilityBasisDTO(
                0.002,
                0,
                true,
                "NEM reliability standard"));
            result.StorageSizing.Should().BeEquivalentTo(new StorageSizingOutcomeDTO(
                StorageSizingOutcome.NotRequired,
                includesStorage ? 120 : 0,
                includesStorage ? 30 : 0,
                includesStorage ? 120 : 0,
                includesStorage ? 30 : 0,
                100_000,
                10_000,
                1,
                null,
                []));
            result.DataSources.WeatherBasis.Kind.Should().Be(WeatherBasisKind.TypicalMeteorologicalYear);
            result.Cost.AnnualisedGenerationCostAud.Should().Be(0);
            result.Cost.AnnualisedStorageCostAud.Should().Be(0);
            result.Cost.SlcoeAudPerMwh.Should().Be(0);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static FlowSeries Flow(DateTimeOffset start, params double[] initialMegawatts)
    {
        return new FlowSeries(start, TimeSpan.FromHours(1), AnnualValues(start, initialMegawatts));
    }

    private static double[] AnnualValues(DateTimeOffset start, params double[] initialValues)
    {
        int hours = (int)(start.AddYears(1) - start).TotalHours;
        var values = new double[hours];
        initialValues.CopyTo(values, 0);
        return values;
    }

    private static GenerationCostParameters CreateCostParameters() => new(
        PowerCapacityCost.FromAudPerMwCapacity(0),
        AnnualPowerCapacityCost.FromAudPerMwYear(0),
        GenerationEnergyCost.FromAudPerMwhGenerated(0),
        FuelPrice.FromAudPerGjThermal(0));

    private static GenerationTechnologyProfile CreateTechnologyProfile() => new(
        HeatRate.FromGigajoulesPerMegawattHour(0),
        technicalLifeYears: 30u);
}