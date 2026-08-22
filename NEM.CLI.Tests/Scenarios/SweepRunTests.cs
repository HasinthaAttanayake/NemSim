using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;
using NEM.CLI.Scenarios;
using NEM.Contracts;
using NEM.Model.Grid;
using NEM.Model.Simulation;

namespace NEM.CLI.Tests.Scenarios;

[Trait("Category", "FullYearAcceptance")]
public sealed class SweepRunTests
{
    [Fact]
    public void Run_WritesPublishedIndexAndSucceededPointArtifacts()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        using var output = new StringWriter();
        int exitCode = SweepRunCommand.Run(fixture.CreateContext(output), "sweeps/test-sweep.json");

        exitCode.Should().Be(0);
        File.Exists(fixture.PointResultPath("p0")).Should().BeTrue();
        File.Exists(fixture.PointResultPath("p1")).Should().BeTrue();
        JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p1")))!["schemaVersion"]!
            .GetValue<int>().Should().Be(ArtifactSchemaVersions.SystemDispatchResults);
        File.Exists(fixture.RegionResultPath("p0", "NSW1")).Should().BeTrue();
        File.Exists(fixture.RegionResultPath("p1", "NSW1")).Should().BeTrue();
        Status(fixture, "p0")["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, "p1")["status"]!.GetValue<string>().Should().Be("succeeded");
        JsonObject index = ReadIndex(fixture);
        index["schemaVersion"]!.GetValue<int>().Should().Be(ArtifactSchemaVersions.SweepIndex);
        index["points"]!.AsArray().Should().HaveCount(2);
        JsonObject firstPoint = index["points"]![0]!.AsObject();
        firstPoint["status"]!.GetValue<string>().Should().Be("succeeded");
        firstPoint["detailPath"]!.GetValue<string>().Should().Be("points/p0.json");
        firstPoint["regionScalars"]!.AsArray().Should().ContainSingle();
        firstPoint["regionScalars"]![0]!["regionId"]!.GetValue<string>().Should().Be("NSW1");
        firstPoint["regionDetails"]!.AsArray().Should().ContainSingle();
        firstPoint["regionDetails"]![0]!["detailPath"]!.GetValue<string>()
            .Should().Be("points/p0-nsw1.json");
        firstPoint["configPath"]!.GetValue<string>().Should().Be("configs/p0.json");
        firstPoint["scalars"]!["energyServedMwh"]!.GetValue<double>().Should().Be(87_600);
        firstPoint["scalars"]!["achievedRenewableShareGridScale"]!.GetValue<double>().Should().Be(0);
        firstPoint["scalars"]!["achievedRenewableShareNative"]!.GetValue<double>().Should().Be(0);
        firstPoint["scalars"]!["unservedHours"]!.GetValue<int>().Should().Be(0);
        firstPoint["reliability"]!["targetUsePercentageOfDemand"]!.GetValue<double>()
            .Should().Be(0.002);
        firstPoint["reliability"]!["withinTarget"]!.GetValue<bool>().Should().BeTrue();
        firstPoint["storageSizing"]!["outcome"]!.GetValue<string>().Should().Be("notRequired");
        firstPoint["intervalPointers"]!.AsObject().Select(pointer => pointer.Key)
            .Should().Contain("peakUnservedIntervalIndex");
        index["scope"]!["regionIds"]!.AsArray().Select(region => region!.GetValue<string>())
            .Should().Equal("NSW1");
        index["scope"]!["resolution"]!.GetValue<string>().Should().Be("01:00:00");
        index["scope"]!["weatherBasis"]!["kind"]!.GetValue<string>()
            .Should().Be("typicalMeteorologicalYear");
        string[] inputPurposes = index["provenance"]!["inputFiles"]!.AsArray()
            .Select(input => input!["purpose"]!.GetValue<string>()).ToArray();
        inputPurposes.Should().Contain(["demand-data", "weather-data", "sweep-definition"]);
        inputPurposes.Should().NotContain("emitted-scenario-config");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        manifest["schemaVersion"]!.GetValue<int>().Should().Be(1);
        manifest["sweeps"]!.AsArray().Should().ContainSingle().Which!["sweepId"]!.GetValue<string>()
            .Should().Be("test-sweep");
        new FileInfo(fixture.IndexPath).Length.Should().BeLessThan(10_000);
        File.Exists(fixture.SharedBaseSeriesPath).Should().BeTrue();
        JsonObject pointResult = JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p0")))!.AsObject();
        pointResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        pointResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>()
            .Should().StartWith("../series/base-demand-");
        JsonObject regionResult = JsonNode.Parse(File.ReadAllText(
            fixture.RegionResultPath("p0", "NSW1")))!.AsObject();
        regionResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        regionResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>()
            .Should().StartWith("../series/base-demand-");
        output.ToString().Should().Contain("Running sweep point p1 (Capacity=1 MW).");
    }

    [Fact]
    public void Run_RecordsPerPointAndTotalDurationInIndex()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json").Should().Be(0);

        JsonObject index = ReadIndex(fixture);
        double totalDurationMs = index["provenance"]!["totalDurationMs"]!.GetValue<double>();
        totalDurationMs.Should().BeGreaterThan(0);
        double sumOfPointDurations = 0;
        foreach (JsonNode? point in index["points"]!.AsArray())
        {
            double pointDurationMs = point!["durationMs"]!.GetValue<double>();
            pointDurationMs.Should().BeGreaterThan(0);
            sumOfPointDurations += pointDurationMs;
        }

        sumOfPointDurations.Should().BeLessThanOrEqualTo(totalDurationMs);
    }

    [Fact]
    public void Run_DeletesSeriesFilesNoPointReferences()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition(
            """[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        string stalePath = Path.Combine(fixture.SweepDataPath, "series", "base-demand-stale.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stalePath)!);
        File.WriteAllText(stalePath, "{}");

        SweepRunCommand.Run(fixture.CreateContext(TextWriter.Null), "sweeps/test-sweep.json")
            .Should().Be(0);

        File.Exists(stalePath).Should().BeFalse();
        File.Exists(fixture.SharedBaseSeriesPath).Should().BeTrue();
    }

    [Fact]
    public void Run_ContinuesAfterPointFailureAndReturnsNonZeroSummary()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Invalid", "overrides": { "costBasis": { "year": 1999 } } }]
            """);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.PointResultPath("p1"))!);
        File.WriteAllText(fixture.PointResultPath("p1"), "stale result");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = SweepRunCommand.Run(fixture.CreateContext(output, error), "sweeps/test-sweep.json");

        exitCode.Should().Be(1);
        File.Exists(fixture.PointResultPath("p0")).Should().BeTrue();
        File.Exists(fixture.PointResultPath("p1")).Should().BeFalse();
        Status(fixture, "p0")["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, "p1")["status"]!.GetValue<string>().Should().Be("failed");
        JsonObject failedPoint = ReadIndex(fixture)["points"]![1]!.AsObject();
        failedPoint["status"]!.GetValue<string>().Should().Be("failed");
        failedPoint["detailPath"].Should().BeNull();
        failedPoint["scalars"].Should().BeNull();
        failedPoint["failure"]!["stage"]!.GetValue<string>().Should().Be("input");
        failedPoint["failure"]!["code"]!.GetValue<string>().Should().Be("invalidConfig");
        failedPoint["failure"]!["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        output.ToString().Should().NotContain("failed");
        error.ToString().Should().Contain("Sweep point p1: failed:");
        error.ToString().Should().Contain("failed points: p1");
    }

    /// <summary>
    /// A malformed override can fail before a config is even generated, not just after: a keyed
    /// array override missing its key property fails inside the JSON merge patch itself. That used
    /// to happen in an unguarded fan-out pass before any point's dispatch even started, aborting the
    /// whole run with no results published for any point. It must now isolate to the one point.
    /// </summary>
    [Fact]
    public void Run_ContinuesAfterAnOverrideFailsToMergeIntoTheBaseline()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Malformed", "overrides": { "regions": [{ "generatingFleets": [] }] } }]
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = SweepRunCommand.Run(fixture.CreateContext(output, error), "sweeps/test-sweep.json");

        exitCode.Should().Be(1);
        File.Exists(fixture.PointResultPath("p0")).Should().BeTrue();
        File.Exists(fixture.PointResultPath("p1")).Should().BeFalse();
        Status(fixture, "p0")["status"]!.GetValue<string>().Should().Be("succeeded");
        Status(fixture, "p1")["status"]!.GetValue<string>().Should().Be("failed");
        JsonObject failedPoint = ReadIndex(fixture)["points"]![1]!.AsObject();
        failedPoint["status"]!.GetValue<string>().Should().Be("failed");
        failedPoint["failure"]!["stage"]!.GetValue<string>().Should().Be("input");
        failedPoint["failure"]!["code"]!.GetValue<string>().Should().Be("invalidConfig");
        error.ToString().Should().Contain("failed points: p1");
    }

    [Fact]
    public void Run_ExternalizesBothSystemAndRegionalDemandToTheSameSharedSeriesFile()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """);
        CliContext context = fixture.CreateContext(TextWriter.Null);

        SweepRunCommand.Run(context, "sweeps/test-sweep.json").Should().Be(0);

        JsonObject pointResult = JsonNode.Parse(File.ReadAllText(fixture.PointResultPath("p1")))!.AsObject();
        pointResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        string pointSeriesPath = pointResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>();
        pointSeriesPath.Should().StartWith("../series/base-demand-");
        JsonObject regionResult = JsonNode.Parse(File.ReadAllText(
            fixture.RegionResultPath("p1", "NSW1")))!.AsObject();
        regionResult["dataSeries"]!["demand"]!["baseDemandMw"].Should().BeNull();
        string regionSeriesPath = regionResult["dataSeries"]!["demand"]!["baseDemandSeriesPath"]!.GetValue<string>();
        regionSeriesPath.Should().StartWith("../series/base-demand-");
        // A single-region sweep's system-wide demand equals its one region's demand, so both
        // point and regional results should reference the same content-addressed series file.
        regionSeriesPath.Should().Be(pointSeriesPath);
    }

    [Fact]
    public void Run_IsDeterministicAcrossCleanDirectoriesExceptGeneratedAt()
    {
        using var first = new SweepRunFixture();
        using var second = new SweepRunFixture();
        const string points = """
            [{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} },
             { "pointId": "p1", "axisValue": 1, "label": "Changed", "overrides": { "name": "Changed scenario" } }]
            """;
        first.WriteDefinition(points);
        second.WriteDefinition(points);

        SweepRunCommand.Run(first.CreateContext(TextWriter.Null), "sweeps/test-sweep.json").Should().Be(0);
        SweepRunCommand.Run(second.CreateContext(TextWriter.Null), "sweeps/test-sweep.json").Should().Be(0);

        Dictionary<string, byte[]> firstFiles = SweepFiles(first);
        Dictionary<string, byte[]> secondFiles = SweepFiles(second);
        firstFiles.Keys.Should().Equal(secondFiles.Keys);
        foreach (string path in firstFiles.Keys)
        {
            firstFiles[path].Should().Equal(secondFiles[path]);
        }
    }

    [Fact]
    public void CreateProvenance_ChangesDemandHashWhenTheInputBytesChange()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(TextWriter.Null);
        SweepDefinition definition = SweepFanOutCommand.WriteConfigs(
            context,
            "sweeps/test-sweep.json",
            validateGeneratedConfigs: false);
        string configPath = Path.Combine(
            fixture.RootPath,
            "sweeps",
            "test-sweep",
            "configs",
            "p0.json");

        SweepProvenanceDTO first = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));
        File.AppendAllText(Path.Combine(fixture.RootPath, "demand.json"), Environment.NewLine);
        SweepProvenanceDTO second = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));

        first.InputFiles.Single(input => input.Purpose == "demand-data").Sha256.Should().NotBe(
            second.InputFiles.Single(input => input.Purpose == "demand-data").Sha256);
    }

    [Fact]
    public void CreateProvenance_ListsDemandAndWeatherInputsForEveryRegion()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteTwoRegionBaseline();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(TextWriter.Null);
        SweepDefinition definition = SweepFanOutCommand.WriteConfigs(
            context,
            "sweeps/test-sweep.json",
            validateGeneratedConfigs: false);
        string configPath = Path.Combine(
            fixture.RootPath,
            "sweeps",
            "test-sweep",
            "configs",
            "p0.json");

        SweepProvenanceDTO provenance = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));

        provenance.InputFiles.Count(input => input.Purpose == "demand-data").Should().Be(2);
        provenance.InputFiles.Count(input => input.Purpose == "weather-data").Should().Be(2);
    }

    [Fact]
    public void CreateProvenance_UsesConfiguredOutputRootForScenarioInputs()
    {
        using var fixture = new SweepRunFixture();
        fixture.MoveInputsToConfiguredOutputRoot();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(TextWriter.Null);
        SweepDefinition definition = SweepFanOutCommand.WriteConfigs(
            context,
            "sweeps/test-sweep.json",
            validateGeneratedConfigs: false);
        string configPath = Path.Combine(
            fixture.RootPath,
            "sweeps",
            "test-sweep",
            "configs",
            "p0.json");

        SweepProvenanceDTO provenance = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            fixture.DefinitionPath,
            [configPath],
            new SweepRunMetadata("test", false));

        provenance.InputFiles.Should().Contain(
            input => input.Purpose == "demand-data" && input.Path == "published-inputs/demand.json");
        provenance.InputFiles.Should().Contain(
            input => input.Purpose == "weather-data" && input.Path == "published-inputs/weather.json");
    }

    [Fact]
    public void CreateProvenance_DistinguishesCloseEconomicValuesInResolvedDefinition()
    {
        using var fixture = new SweepRunFixture();
        fixture.WriteDefinition("""[{ "pointId": "p0", "axisValue": 0, "label": "Base", "overrides": {} }]""");
        CliContext context = fixture.CreateContext(TextWriter.Null);
        SweepDefinition definition = SweepDefinition.Load("sweeps/test-sweep.json", fixture.Paths);
        SweepDefinition firstDefinition = definition with
        {
            Points = [definition.Points[0] with
            {
                Overrides = new JsonObject { ["realDiscountRate"] = 0.070001d },
            }],
        };
        SweepDefinition secondDefinition = definition with
        {
            Points = [definition.Points[0] with
            {
                Overrides = new JsonObject { ["realDiscountRate"] = 0.070002d },
            }],
        };

        SweepProvenanceDTO first = SweepArtifactExport.CreateProvenance(
            context,
            firstDefinition,
            fixture.DefinitionPath,
            [],
            new SweepRunMetadata("test", false));
        SweepProvenanceDTO second = SweepArtifactExport.CreateProvenance(
            context,
            secondDefinition,
            fixture.DefinitionPath,
            [],
            new SweepRunMetadata("test", false));

        first.ResolvedDefinitionSha256.Should().NotBe(second.ResolvedDefinitionSha256);
    }

    [Fact]
    public void CreateScalars_UsesDemandMinusUnservedEnergyWhenGenerationChargesStorage()
    {
        var result = new DispatchResultsDTO(
            ArtifactSchemaVersions.DispatchResults,
            new DispatchScenarioDTO("test", "Test", "NSW1", DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddHours(1), TimeSpan.FromHours(1)),
            DateTimeOffset.UnixEpoch,
            new DispatchSourcesDTO(
                new DispatchInputArtifactDTO("demand.json", 2, "demand"),
                new DispatchInputArtifactDTO("weather.json", 5, "weather"),
                new WeatherBasisDTO(
                    WeatherBasisKind.TypicalMeteorologicalYear,
                    new WeatherSiteDTO("test-solar.epw", "Test solar"),
                    new WeatherSiteDTO("test-wind.epw", "Test wind"),
                    "Typical meteorological year from test.epw."),
                []),
            new DispatchPowerSystemDTO("test", [], [new DispatchStorageFleetDTO("Battery", 20, 20)]),
            new DispatchSeriesDTO(
                new DispatchDemandDTO([100], new Dictionary<string, double[]> { ["Data centres"] = [50] }, [150]),
                new Dictionary<string, double[]> { ["Solar"] = [40], ["Wind"] = [20], ["Hydro"] = [30], ["Gas"] = [30] },
                [0],
                [10],
                [20],
                [0],
                new Dictionary<string, double[]> { ["Battery"] = [0] },
                [0],
                [0],
                [0]),
            new DispatchMetricsDTO(150, 120, 0, 10, 10.0 / 150 * 100, 1, 0, 10, new IntervalPointersDTO(0, null, 0)),
            new ReliabilityBasisDTO(0.002, 10, false, "NEM reliability standard"),
            new StorageSizingOutcomeDTO(StorageSizingOutcome.Resized, 10, 10, 20, 20, 400, 100, 2),
            new DispatchCostDTO(
                "calculated", 0, 0, 0, 0, 0, 0, 0, 0,
                TransmissionCostStatus.NotModelled, 0, []));

        SweepPointScalarResultsDTO scalars = SweepArtifactExport.CreateScalars(result);
        RenewableShareMetrics sourceMetrics = RenewableShareMetrics.FromDeliveredEnergy(
            new Dictionary<GenerationTechnology, double>
            {
                [GenerationTechnology.Solar] = 40,
                [GenerationTechnology.Wind] = 20,
                [GenerationTechnology.Hydro] = 30,
                [GenerationTechnology.Gas] = 30,
            },
            100);

        scalars.EnergyServedMwh.Should().Be(140);
        scalars.DemandMwh.Should().Be(150);
        scalars.DeliveredGenerationMwh.Should().Be(120);
        scalars.AchievedRenewableShareGridScale.Should().Be(sourceMetrics.GridScaleShare);
        scalars.AchievedRenewableShareNative.Should().Be(sourceMetrics.NativeShare);
        scalars.UnservedHours.Should().Be(1);
        scalars.PeakUnservedPowerMw.Should().Be(10);
    }

    private static JsonObject Status(SweepRunFixture fixture, string pointId) =>
        JsonNode.Parse(File.ReadAllText(fixture.PointStatusPath(pointId)))!.AsObject();

    private static JsonObject ReadIndex(SweepRunFixture fixture) =>
        JsonNode.Parse(File.ReadAllText(fixture.IndexPath))!.AsObject();

    private static byte[] NormalizedResultBytes(string path)
    {
        JsonObject result = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        result.Remove("generatedAt");
        result.Remove("runId");
        return Encoding.UTF8.GetBytes(JsonFile.Serialize(result));
    }

    /// <summary>Strips wall-clock run-timing figures, which genuinely vary run to run.</summary>
    private static byte[] NormalizedIndexBytes(string path)
    {
        JsonObject index = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        index["provenance"]!.AsObject().Remove("totalDurationMs");
        foreach (JsonNode? point in index["points"]!.AsArray())
        {
            point!.AsObject().Remove("durationMs");
        }

        return Encoding.UTF8.GetBytes(JsonFile.Serialize(index));
    }

    private static void RestoreExternalizedBaseDemand(string pointResultPath, JsonObject result)
    {
        JsonObject demand = result["dataSeries"]!["demand"]!.AsObject();
        string relativePath = demand["baseDemandSeriesPath"]!.GetValue<string>();
        string seriesPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(pointResultPath)!,
            relativePath));
        JsonArray values = JsonNode.Parse(File.ReadAllText(seriesPath))!["valuesMw"]!.AsArray();
        demand["baseDemandMw"] = values.DeepClone();
        demand["baseDemandSeriesPath"] = null;
    }

    private static Dictionary<string, byte[]> SweepFiles(SweepRunFixture fixture)
    {
        string sweepPath = fixture.SweepDataPath;
        return Directory.GetFiles(sweepPath, "*.json", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(sweepPath, path),
                path => path.Contains($"{Path.DirectorySeparatorChar}points{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.EndsWith(".status.json", StringComparison.Ordinal)
                    ? NormalizedResultBytes(path)
                    : path.EndsWith("index.json", StringComparison.Ordinal)
                        ? NormalizedIndexBytes(path)
                        : File.ReadAllBytes(path),
                StringComparer.Ordinal);
    }

    private sealed class SweepRunFixture : IDisposable
    {
        private const int HoursPerYear = 8_760;

        public SweepRunFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"nemsim-sweep-run-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(RootPath, "sweeps"));
            Directory.CreateDirectory(Path.Combine(RootPath, "scenarios"));
            File.WriteAllText(Path.Combine(RootPath, "NemSim.slnx"), string.Empty);
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));
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
            File.WriteAllText(Path.Combine(RootPath, "scenarios", "baseline.json"), """
            { "schemaVersion": 4, "id": "baseline", "name": "Baseline", "costBasis": { "year": 2026, "realDiscountRate": 0.07 }, "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 }, "regions": [{ "regionId": "NSW1", "demandFile": "demand.json", "weatherFile": "weather.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }] }
            """);
            Paths = RepositoryPaths.Discover(RootPath);
        }

        public string RootPath { get; }
        public RepositoryPaths Paths { get; }

        public CliContext CreateContext(TextWriter output, TextWriter? error = null) =>
            new(Paths, RootPath, output, error);

        public string PointResultPath(string pointId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}.json");

        public string RegionResultPath(string pointId, string regionId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}-{regionId.ToLowerInvariant()}.json");

        public string PointStatusPath(string pointId) => Path.Combine(
            SweepDataPath, "points", $"{pointId}.status.json");

        public string SweepDataPath => Path.Combine(
            RootPath, "NEM.Web", "wwwroot", "data", "sweeps", "test-sweep");

        public string IndexPath => Path.Combine(SweepDataPath, "index.json");

        public string ManifestPath => Path.Combine(
            RootPath, "NEM.Web", "wwwroot", "data", "sweeps", "index.json");

        public string DefinitionPath => Path.Combine(RootPath, "sweeps", "test-sweep.json");

        public string SharedBaseSeriesPath => Directory.GetFiles(
            Path.Combine(SweepDataPath, "series"),
            "base-demand-*.json",
            SearchOption.TopDirectoryOnly).Single();

        public void WriteDefinition(string points) => File.WriteAllText(
            Path.Combine(RootPath, "sweeps", "test-sweep.json"),
            $$"""
            { "schemaVersion": 1, "sweepId": "test-sweep", "name": "Test sweep", "axis": { "label": "Capacity", "unit": "MW" }, "baselineConfigPath": "scenarios/baseline.json", "points": {{points}} }
            """);

        public void WriteTwoRegionBaseline()
        {
            File.Copy(
                Path.Combine(RootPath, "demand.json"),
                Path.Combine(RootPath, "demand-vic1.json"));
            File.Copy(
                Path.Combine(RootPath, "weather.json"),
                Path.Combine(RootPath, "weather-vic1.json"));
            File.WriteAllText(Path.Combine(RootPath, "scenarios", "baseline.json"), """
            { "schemaVersion": 4, "id": "baseline", "name": "Baseline", "costBasis": { "year": 2026, "realDiscountRate": 0.07 }, "storageSizing": { "maximumPowerMw": 100, "maximumEnergyMwh": 400 }, "regions": [{ "regionId": "NSW1", "demandFile": "demand.json", "weatherFile": "weather.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }, { "regionId": "VIC1", "demandFile": "demand-vic1.json", "weatherFile": "weather-vic1.json", "generatingFleets": [{ "technology": "Gas", "nameplateCapacityMw": 100, "costParameters": { "capitalCostAudPerMw": 0, "fixedOperatingCostAudPerMwYear": 0, "variableOperatingCostAudPerMwh": 0, "fuelPriceAudPerGj": 0 }, "technologyProfile": { "heatRateGjPerMwh": 7, "technicalLifeYears": 30 } }], "storageFleets": [{ "technology": "Battery", "initialEnergyCapacityMwh": 0, "initialPowerCapacityMw": 0, "costParameters": { "powerCapitalCostAudPerMw": 0, "energyCapitalCostAudPerMwh": 0, "fixedOperatingCostAudPerMwYear": 0 }, "technologyProfile": { "technicalLifeYears": 15, "roundTripEfficiency": 0.87 } }] }] }
            """);
        }

        public void MoveInputsToConfiguredOutputRoot()
        {
            const string outputDirectory = "published-inputs";
            string fullOutputDirectory = Path.Combine(RootPath, outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);
            File.Move(
                Path.Combine(RootPath, "demand.json"),
                Path.Combine(fullOutputDirectory, "demand.json"));
            File.Move(
                Path.Combine(RootPath, "weather.json"),
                Path.Combine(fullOutputDirectory, "weather.json"));
            string settingsDirectory = Path.Combine(RootPath, "NEM.CLI");
            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(
                Path.Combine(settingsDirectory, "appsettings.local.json"),
                """{ "inputBundleRoot": "unused", "outputRoot": "published-inputs", "defaultScenarioPath": "unused" }""");
        }

        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}