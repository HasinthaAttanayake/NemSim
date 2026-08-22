using System.Text.Json;
using AwesomeAssertions;
using NEM.Contracts;

namespace NEM.CLI.Tests.Contracts;

public sealed class SystemAndRegionDispatchResultsContractTests
{
    [Fact]
    public void SystemDispatchResults_RoundTripsPopulatedEvidenceWithExplicitUnits()
    {
        DateTimeOffset start = new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        SystemDispatchResultsDTO result = CreateSystemResult(start);

        string json = JsonSerializer.Serialize(result, CamelCaseOptions);
        SystemDispatchResultsDTO? roundTripped = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
            json,
            CaseInsensitiveOptions);

        roundTripped.Should().BeEquivalentTo(result);
        roundTripped!.SchemaVersion.Should().Be(ArtifactSchemaVersions.SystemDispatchResults);
        json.Should().Contain("\"runId\"");
        json.Should().Contain("\"periodStart\"");
        json.Should().Contain("\"periodEnd\"");
        json.Should().Contain("\"resolution\"");
        json.Should().Contain("\"regionIds\"");
        json.Should().Contain("\"dataSourcesByRegion\"");
        json.Should().Contain("\"regionSummariesById\"");
        json.Should().Contain("\"deliveredGenerationByTechnologyMwh\"");
        json.Should().Contain("\"dataSeries\"");
        json.Should().Contain("\"totalDemandMw\"");
        json.Should().Contain("\"demandMwh\"");
        json.Should().Contain("\"targetUsePercentageOfDemand\"");
        json.Should().Contain("\"totalAnnualisedCostAud\"");
        json.Should().Contain("\"annualisedTransmissionCostAud\"");
        json.Should().Contain("\"transmissionSlcotAudPerMwh\"");
        json.Should().Contain("\"interconnectors\"");
        json.Should().Contain("\"capacityMw\"");
        json.Should().Contain("\"flowMw\"");
        json.Should().Contain("\"lossesMw\"");
        json.Should().Contain("\"distanceKm\"");
        json.Should().Contain("\"fromLatitude\"");
        json.Should().Contain("\"fromLongitude\"");
        json.Should().Contain("\"toLatitude\"");
        json.Should().Contain("\"toLongitude\"");
        json.Should().Contain("\"finalEnergyMwh\"");
        json.Should().Contain("\"sha256\"");
        json.Should().Contain("\"outcome\":\"resized\"");
        json.Should().Contain("\"energyLimitedEvidence\"");
        json.Should().Contain("\"shortfallEnergyGwh\"");
        json.Should().Contain("\"bindingIntervalIndices\"");
        json.Should().Contain("\"peakUnservedIntervalIndex\"");
    }

    [Fact]
    public void RegionDispatchResults_RoundTripsPopulatedDetailAndPreservesRunIdentity()
    {
        DateTimeOffset start = new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        RegionDispatchResultsDTO result = CreateRegionResult(start, "NSW1");

        string json = JsonSerializer.Serialize(result, CamelCaseOptions);
        RegionDispatchResultsDTO? roundTripped = JsonSerializer.Deserialize<RegionDispatchResultsDTO>(
            json,
            CaseInsensitiveOptions);

        roundTripped.Should().BeEquivalentTo(result);
        roundTripped!.SchemaVersion.Should().Be(ArtifactSchemaVersions.RegionDispatchResults);
        roundTripped.RunId.Should().Be("run-2026-07-01");
        roundTripped.RegionId.Should().Be("NSW1");
        json.Should().Contain("\"regionId\"");
        json.Should().Contain("\"powerSystem\"");
        json.Should().Contain("\"fleets\"");
        json.Should().Contain("\"nameplateCapacityMw\"");
        json.Should().Contain("\"deliveredGenerationByTechnologyMw\"");
        json.Should().Contain("\"stateOfChargeByTechnologyMwh\"");
        json.Should().Contain("\"slcoeAudPerMwh\"");
        json.Should().Contain("\"outcome\"");
    }

    [Fact]
    public void RegionDispatchOverview_RoundTripsPopulatedFactsWithNoIntervalSeries()
    {
        DateTimeOffset start = new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        RegionDispatchOverviewDTO overview = new(
            ArtifactSchemaVersions.RegionDispatchOverview,
            "run-2026-07-01",
            "NSW1",
            start,
            start.AddHours(2),
            TimeSpan.FromHours(1),
            CreateSources("nsw-demand.json"),
            new DispatchPowerSystemDTO(
                "system-2026",
                [new DispatchFleetDTO("Solar", 100)],
                [new DispatchStorageFleetDTO("Battery", 120, 30)]),
            CreateMetrics(),
            new ReliabilityBasisDTO(0.002, 0, true, "NEM reliability standard"),
            CreateSizing(),
            CreateCost(),
            new Dictionary<string, double> { ["Solar"] = 165 },
            1.5);

        string json = JsonSerializer.Serialize(overview, CamelCaseOptions);
        RegionDispatchOverviewDTO? roundTripped = JsonSerializer.Deserialize<RegionDispatchOverviewDTO>(
            json,
            CaseInsensitiveOptions);

        roundTripped.Should().BeEquivalentTo(overview);
        roundTripped!.SchemaVersion.Should().Be(ArtifactSchemaVersions.RegionDispatchOverview);
        json.Should().Contain("\"regionId\"");
        json.Should().Contain("\"deliveredGenerationByTechnologyMwh\"");
        json.Should().Contain("\"transmissionLossesMwh\"");
        json.Should().NotContain("\"dataSeries\"");
    }

    [Fact]
    public void RegionDispatchSummary_CarriesAnOverviewPathBesideItsDetailPath()
    {
        DateTimeOffset start = new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));
        SystemDispatchResultsDTO result = CreateSystemResult(start);

        string json = JsonSerializer.Serialize(result, CamelCaseOptions);
        SystemDispatchResultsDTO? roundTripped = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
            json,
            CaseInsensitiveOptions);

        roundTripped.Should().BeEquivalentTo(result);
        roundTripped!.RegionSummariesById["NSW1"].OverviewPath.Should().Be("results-nsw1-overview.json");
        json.Should().Contain("\"overviewPath\"");
    }

    private static SystemDispatchResultsDTO CreateSystemResult(DateTimeOffset start)
    {
        RegionDispatchSummaryDTO summary = new(
            CreateMetrics(),
            new ReliabilityBasisDTO(0.002, 0, true, "NEM reliability standard"),
            CreateSizing(),
            CreateCost(),
            new Dictionary<string, double> { ["Solar"] = 165 },
            "results-nsw1.json",
            "results-nsw1-overview.json");

        return new SystemDispatchResultsDTO(
            ArtifactSchemaVersions.SystemDispatchResults,
            "run-2026-07-01",
            start,
            start.AddHours(2),
            TimeSpan.FromHours(1),
            ["NSW1", "VIC1"],
            new Dictionary<string, DispatchSourcesDTO>
            {
                ["NSW1"] = CreateSources("nsw-demand.json"),
                ["VIC1"] = CreateSources("vic-demand.json"),
            },
            new Dictionary<string, RegionDispatchSummaryDTO>
            {
                ["NSW1"] = summary,
                ["VIC1"] = summary,
            },
            CreateSeries(),
            CreateMetrics(),
            new ReliabilityBasisDTO(0.002, 0, true, "NEM reliability standard"),
            CreateSizing(),
            CreateCost(),
            new DispatchTopologyDTO(
                ["NSW1", "VIC1"],
                [new DispatchTopologyLinkDTO("NSW1->VIC1", "NSW1", "VIC1", 100)]),
            [new DispatchInterconnectorDTO(
                "NSW1->VIC1",
                "NSW1",
                "VIC1",
                100,
                [20, 0],
                [1, 0.5],
                714.2,
                -33.9,
                151.2,
                -37.8,
                144.9)]);
    }

    private static RegionDispatchResultsDTO CreateRegionResult(DateTimeOffset start, string regionId) =>
        new(
            ArtifactSchemaVersions.RegionDispatchResults,
            "run-2026-07-01",
            regionId,
            start,
            start.AddHours(2),
            TimeSpan.FromHours(1),
            CreateSources("nsw-demand.json"),
            new DispatchPowerSystemDTO(
                "system-2026",
                [new DispatchFleetDTO("Solar", 100)],
                [new DispatchStorageFleetDTO("Battery", 120, 30)]),
            CreateSeries(),
            CreateMetrics(),
            new ReliabilityBasisDTO(0.002, 0, true, "NEM reliability standard"),
            CreateSizing(),
            CreateCost());

    private static DispatchSourcesDTO CreateSources(string demandFile) =>
        new(
            new DispatchInputArtifactDTO(demandFile, 2, new string('a', 64)),
            new DispatchInputArtifactDTO("weather.json", 6, new string('b', 64)),
            new WeatherBasisDTO(
                WeatherBasisKind.TypicalMeteorologicalYear,
                new WeatherSiteDTO("sydney-solar.epw", "Sydney (WMO 947680)"),
                new WeatherSiteDTO("sydney-wind.epw", "Sydney (WMO 947680)"),
                "Typical meteorological year."),
            ["demand.zip"]);

    private static DispatchSeriesDTO CreateSeries() =>
        new(
            new DispatchDemandDTO([70, 80], new Dictionary<string, double[]> { ["Data centres"] = [10, 10] }, [80, 90]),
            new Dictionary<string, double[]> { ["Solar"] = [80, 85] },
            [20, 15],
            [0, 0],
            [10, 0],
            [0, 5],
            new Dictionary<string, double[]> { ["Battery"] = [0, 8.7] },
            [0, 10],
            [20, 0],
            [1, 0.5]);

    private static DispatchMetricsDTO CreateMetrics() =>
        new(170, 165, 35, 0, 0, 0, 1, 0, new IntervalPointersDTO(1, 0, 0));

    private static StorageSizingOutcomeDTO CreateSizing() =>
        new(
            StorageSizingOutcome.Resized,
            120,
            30,
            120,
            30,
            240,
            60,
            3,
            new EnergyLimitedEvidenceDTO(10, 12, 2, [4, 7]),
            [new StorageSizingPassDTO(1, 100, 25, 5, 2)]);

    private static DispatchCostDTO CreateCost() =>
        new(
            "calculated",
            1000m,
            200m,
            1250m,
            10m,
            2m,
            12.5m,
            50m,
            0.5m,
            TransmissionCostStatus.Calculated,
            9.5,
            [
                new DispatchGenerationCostContributionDTO("Solar", 600m, 6m),
                new DispatchGenerationCostContributionDTO("Coal", 400m, 4m),
            ]);

    private static JsonSerializerOptions CamelCaseOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static JsonSerializerOptions CaseInsensitiveOptions => new()
    {
        PropertyNameCaseInsensitive = true,
    };
}