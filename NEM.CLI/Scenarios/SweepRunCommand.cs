using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;
using NEM.Contracts;

namespace NEM.CLI.Scenarios;

internal static class SweepRunCommand
{
    public static int Run(CliContext context, string definitionPath)
    {
        var runStopwatch = Stopwatch.StartNew();
        SweepRunMetadata runMetadata = SweepArtifactExport.CaptureRunMetadata(context.Paths.SolutionRoot);
        (SweepDefinition definition, JsonNode baseline) =
            SweepFanOutCommand.LoadDefinitionAndBaseline(context, definitionPath);
        string configOutputDirectory = SweepFanOutCommand.ConfigOutputDirectory(context, definition);
        string sweepDirectory = context.Paths.WebDataPath(Path.Combine("sweeps", definition.SweepId));
        string pointsDirectory = Path.Combine(sweepDirectory, "points");
        var failedPointIds = new List<string>();
        var indexPoints = new List<SweepIndexPointDTO>();
        var configPaths = new List<string>();
        var succeededResults = new List<SystemDispatchResultsDTO>();
        var referencedSeriesPaths = new List<string>();

        foreach (SweepPoint point in definition.Points)
        {
            string axisValue = point.AxisValue.ToString("G17", CultureInfo.InvariantCulture);
            string publishedConfigPath = Path.Combine(
                sweepDirectory,
                "configs",
                $"{point.PointId}.json");
            string resultPath = Path.Combine(pointsDirectory, $"{point.PointId}.json");
            string statusPath = Path.Combine(pointsDirectory, $"{point.PointId}.status.json");

            context.Output.WriteLine(
                $"Running sweep point {point.PointId} ({definition.Axis.Label}={axisValue} {definition.Axis.Unit}).");
            int referencedSeriesPathCount = referencedSeriesPaths.Count;
            var pointStopwatch = Stopwatch.StartNew();
            try
            {
                // Generated here, inside the per-point try, so a malformed override for one point
                // (a merge-patch failure or a schema-validation failure) is recorded as that point's
                // failure rather than aborting every other point in an unattended run.
                string configPath = WritePointConfig(definition, baseline, configOutputDirectory, point);
                configPaths.Add(configPath);
                Directory.CreateDirectory(Path.GetDirectoryName(publishedConfigPath)!);
                File.Copy(configPath, publishedConfigPath, overwrite: true);

                ScenarioCommand.Run(context, configPath, resultPath, $"{point.PointId}-");
                pointStopwatch.Stop();
                SystemDispatchResultsDTO systemResult = JsonSerializer.Deserialize<SystemDispatchResultsDTO>(
                    File.ReadAllBytes(resultPath),
                    JsonFile.ReadOptions)
                    ?? throw new FormatException($"Sweep point '{point.PointId}' result is empty.");
                var regionScalars = new List<SweepPointRegionScalarsDTO>();
                var regionDetails = new List<SweepPointRegionDetailDTO>();
                foreach (string regionId in systemResult.RegionIds.Order(StringComparer.Ordinal))
                {
                    RegionDispatchSummaryDTO summary = systemResult.RegionSummariesById.GetValueOrDefault(regionId)
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' has no summary for region '{regionId}'.");
                    string detailPath = summary.DetailPath
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' has no detail path for region '{regionId}'.");
                    string overviewPath = summary.OverviewPath
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' has no overview path for region '{regionId}'.");
                    string regionalResultPath = Path.Combine(pointsDirectory, detailPath);
                    RegionDispatchResultsDTO regionalResult = JsonSerializer.Deserialize<RegionDispatchResultsDTO>(
                        File.ReadAllBytes(regionalResultPath),
                        JsonFile.ReadOptions)
                        ?? throw new FormatException(
                            $"Sweep point '{point.PointId}' regional result for '{regionId}' is empty.");
                    SweepPointScalarResultsDTO regionalScalars =
                        SweepArtifactExport.CreateScalars(regionalResult);
                    string seriesPath = ExternalizeBaseDemand(
                        point.PointId,
                        regionalResultPath,
                        sweepDirectory);
                    if (!string.Equals(regionalResult.RegionId, regionId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FormatException(
                            $"Sweep point '{point.PointId}' regional result '{detailPath}' identifies region "
                            + $"'{regionalResult.RegionId}' instead of '{regionId}'.");
                    }

                    referencedSeriesPaths.Add(seriesPath);
                    regionScalars.Add(new SweepPointRegionScalarsDTO(
                        regionId,
                        regionalScalars));
                    regionDetails.Add(new SweepPointRegionDetailDTO(
                        regionId,
                        $"points/{detailPath}",
                        $"points/{overviewPath}"));
                }

                referencedSeriesPaths.Add(ExternalizeBaseDemand(point.PointId, resultPath, sweepDirectory));
                succeededResults.Add(systemResult);
                JsonFile.Write(
                    new SweepPointStatusFile(
                        point.PointId,
                        point.AxisValue,
                        SweepPointStatus.Succeeded,
                        null),
                    statusPath);
                indexPoints.Add(new SweepIndexPointDTO(
                    point.PointId,
                    point.Label,
                    point.AxisValue,
                    SweepPointStatus.Succeeded,
                    $"points/{point.PointId}.json",
                    $"configs/{point.PointId}.json",
                    SweepArtifactExport.CreateScalars(systemResult),
                    systemResult.Reliability,
                    systemResult.StorageSizing,
                    systemResult.Metrics.IntervalPointers,
                    null,
                    regionScalars.ToArray(),
                    regionDetails.ToArray(),
                    $"points/{point.PointId}-overview.json",
                    pointStopwatch.Elapsed.TotalMilliseconds));
                context.Output.WriteLine($"Sweep point {point.PointId}: succeeded.");
            }
            catch (Exception exception)
            {
                pointStopwatch.Stop();
                referencedSeriesPaths.RemoveRange(
                    referencedSeriesPathCount,
                    referencedSeriesPaths.Count - referencedSeriesPathCount);
                File.Delete(resultPath);
                foreach (string regionalPath in Directory.GetFiles(pointsDirectory, $"{point.PointId}-*.json"))
                {
                    File.Delete(regionalPath);
                }
                SweepPointFailureDTO failure = SweepArtifactExport.CreateFailure(exception);
                JsonFile.Write(
                    new SweepPointStatusFile(
                        point.PointId,
                        point.AxisValue,
                        SweepPointStatus.Failed,
                        failure),
                    statusPath);
                failedPointIds.Add(point.PointId);
                indexPoints.Add(new SweepIndexPointDTO(
                    point.PointId,
                    point.Label,
                    point.AxisValue,
                    SweepPointStatus.Failed,
                    null,
                    $"configs/{point.PointId}.json",
                    null,
                    null,
                    null,
                    null,
                    failure,
                    DurationMs: pointStopwatch.Elapsed.TotalMilliseconds));
                (context.Error ?? TextWriter.Null).WriteLine(
                    $"Sweep point {point.PointId}: failed: {failure.Message} "
                    + $"(stage {failure.Stage}, code {failure.Code})");
            }
        }

        string resolvedDefinitionPath = context.Paths.ResolveConfiguredPath(definitionPath);
        SweepProvenanceDTO provenance = SweepArtifactExport.CreateProvenance(
            context,
            definition,
            resolvedDefinitionPath,
            configPaths,
            runMetadata) with
        {
            TotalDurationMs = runStopwatch.Elapsed.TotalMilliseconds,
        };
        JsonFile.Write(
            new SweepIndexDTO(
                ArtifactSchemaVersions.SweepIndex,
                definition.SweepId,
                definition.Name,
                new SweepAxisDTO(definition.Axis.Label, definition.Axis.Unit),
                SweepArtifactExport.CreateScope(succeededResults),
                provenance,
                indexPoints.ToArray()),
            Path.Combine(sweepDirectory, "index.json"));
        SweepArtifactExport.WriteManifest(context.Paths.WebDataPath("sweeps"));
        // Only once the new index is on disk: a series file is still referenced by the previously
        // published index until that index is replaced.
        SweepArtifactExport.PruneUnreferencedSeries(sweepDirectory, referencedSeriesPaths);

        if (failedPointIds.Count == 0)
        {
            context.Output.WriteLine($"Sweep {definition.SweepId} completed.");
            return 0;
        }

        (context.Error ?? TextWriter.Null).WriteLine(
            $"Sweep {definition.SweepId} completed with failed points: {string.Join(", ", failedPointIds)}.");
        return 1;
    }

    /// <summary>Generates and validates one point's scenario config, attributing any failure to the
    /// input stage so it surfaces as that point's failure rather than an unhandled crash.</summary>
    private static string WritePointConfig(
        SweepDefinition definition,
        JsonNode baseline,
        string outputDirectory,
        SweepPoint point)
    {
        try
        {
            return SweepFanOutCommand.WritePointConfig(
                definition,
                baseline,
                outputDirectory,
                point,
                validate: true);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Input,
                "invalidConfig",
                exception.Message,
                exception);
        }
    }

    /// <summary>
    /// Moves the point's base-demand series into the shared series directory and reads the point
    /// back. Failures here are export failures: the point itself ran.
    /// </summary>
    private static string ExternalizeBaseDemand(
        string pointId,
        string resultPath,
        string sweepDirectory)
    {
        try
        {
            string seriesPath = SweepArtifactExport.ExternalizeBaseDemand(resultPath, sweepDirectory);
            return seriesPath;
        }
        catch (Exception exception) when (exception
            is FormatException or IOException or JsonException)
        {
            throw new ScenarioRunException(
                SweepFailureStage.Export,
                "pointArtifactUnusable",
                exception.Message,
                exception);
        }
    }

    /// <summary>Per-point status sidecar, written next to each point's detail artifact.</summary>
    private sealed record SweepPointStatusFile(
        string PointId,
        double AxisValue,
        SweepPointStatus Status,
        SweepPointFailureDTO? Failure);
}