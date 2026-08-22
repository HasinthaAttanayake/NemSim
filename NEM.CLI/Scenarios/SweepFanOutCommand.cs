using System.Text.Json.Nodes;
using NEM.CLI.Application;
using NEM.CLI.Configuration;
using NEM.CLI.Infrastructure;

namespace NEM.CLI.Scenarios;

internal static class SweepFanOutCommand
{
    public static int Run(CliContext context, string definitionPath)
    {
        WriteConfigs(context, definitionPath, validateGeneratedConfigs: true);
        return 0;
    }

    internal static SweepDefinition WriteConfigs(
        CliContext context,
        string definitionPath,
        bool validateGeneratedConfigs)
    {
        (SweepDefinition definition, JsonNode baseline) = LoadDefinitionAndBaseline(context, definitionPath);
        string outputDirectory = ConfigOutputDirectory(context, definition);

        foreach (SweepPoint point in definition.Points)
        {
            string outputPath = WritePointConfig(
                definition,
                baseline,
                outputDirectory,
                point,
                validateGeneratedConfigs);
            context.Output.WriteLine($"Wrote scenario config: {Path.GetFullPath(outputPath)}");
        }

        return definition;
    }

    /// <summary>Reads a sweep's definition and its parsed baseline config, shared by every point.</summary>
    internal static (SweepDefinition Definition, JsonNode Baseline) LoadDefinitionAndBaseline(
        CliContext context,
        string definitionPath)
    {
        SweepDefinition definition = SweepDefinition.Load(definitionPath, context.Paths);
        JsonNode baseline = JsonNode.Parse(File.ReadAllBytes(definition.BaselineConfigFullPath(context.Paths)))
            ?? throw new FormatException($"Sweep '{definition.SweepId}': baseline config is empty.");
        return (definition, baseline);
    }

    internal static string ConfigOutputDirectory(CliContext context, SweepDefinition definition) =>
        Path.Combine(context.Paths.SolutionRoot, "sweeps", definition.SweepId, "configs");

    /// <summary>
    /// Applies one point's overrides to the sweep baseline and writes the resulting scenario config.
    /// Merge-patch failures (a malformed override) and, when requested, scenario schema failures both
    /// surface here, so a caller iterating points one at a time can attribute either to that point.
    /// </summary>
    internal static string WritePointConfig(
        SweepDefinition definition,
        JsonNode baseline,
        string outputDirectory,
        SweepPoint point,
        bool validate)
    {
        JsonObject config = (JsonObject)JsonMergePatch.Apply(baseline, point.Overrides);
        config["id"] = $"{definition.SweepId}-{point.PointId}";
        config["provenance"] = new JsonObject
        {
            ["sweepId"] = definition.SweepId,
            ["pointId"] = point.PointId,
            ["baselineConfigPath"] = definition.BaselineConfigPath,
        };

        string outputPath = Path.Combine(outputDirectory, $"{point.PointId}.json");
        JsonFile.WriteExact(config, outputPath);
        if (validate)
        {
            _ = ScenarioConfig.Load(outputPath);
        }

        return outputPath;
    }
}