using System.Reflection;
using NEM.CLI.Demand;
using NEM.CLI.Generation;
using NEM.CLI.Infrastructure;
using NEM.CLI.Ingest;
using NEM.CLI.Scenarios;
using NEM.CLI.Weather;
using NEM.Contracts;

namespace NEM.CLI.Application;

/// <summary>
/// Maps a command line onto one command handler. Every command is a flag literal followed by zero
/// to three positional arguments; there is no options parser, because the surface is small enough
/// that a pattern match over the argument array is easier to read than a framework.
/// </summary>
/// <remarks>
/// Exit codes are the contract callers script against: <c>0</c> success, <c>1</c> a command that
/// ran and failed, <c>2</c> a command line this router could not route. Requesting help is a
/// success, so <c>--help</c> writes usage to standard output and returns <c>0</c>, while an
/// unrecognised command line writes the same usage to standard error and returns <c>2</c>.
/// </remarks>
internal sealed class CommandRouter
{
    private readonly CliContext _context;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CommandRouter(
        RepositoryPaths paths,
        string settingsDirectory,
        TextWriter output,
        TextWriter error)
    {
        _context = new CliContext(paths, settingsDirectory, output, error);
        _output = output;
        _error = error;
    }

    /// <summary>Routes one command line and returns the process exit code.</summary>
    public int Run(string[] args)
    {
        try
        {
            return args switch
            {
                ["--help"] or ["-h"] or ["--usage"] => PrintUsage(_output, 0),
                ["--version"] => PrintVersion(),
                ["--run-scenario"] => ScenarioCommand.Run(_context),
                ["--run-scenario", var scenarioConfigPath] => ScenarioCommand.Run(_context, scenarioConfigPath),
                ["--fan-out-sweep", var definitionPath] => SweepFanOutCommand.Run(_context, definitionPath),
                ["--run-sweep", var definitionPath] => SweepRunCommand.Run(_context, definitionPath),
                ["--describe-schema", var format] when format is "scenario" or "sweep" =>
                    SchemaDescriptionCommand.Run(_context, format),
                ["--validate-inputs"] => ValidateInputsCommand.Run(_context),
                ["--validate-inputs", var bundlePath] => ValidateInputsCommand.Run(_context, bundlePath),
                ["--ingest"] => IngestCommand.Run(_context),
                ["--ingest", var bundlePath] => IngestCommand.Run(_context, bundlePath),
                ["--import-demand"] =>
                    OperationalDemandCommand.Run(_context, string.Empty),
                ["--import-demand", var outputDirectory] =>
                    OperationalDemandCommand.Run(_context, outputDirectory),
                ["--generation-information", var path] =>
                    GenerationInformationCommand.Run(_context, path),
                ["--epw-report", var regionId, var solarPath] =>
                    EpwCommands.WriteReport(_context, RequireKnownRegion(regionId), solarPath),
                ["--epw-report", var regionId, var solarPath, var windPath] =>
                    EpwCommands.WriteReport(_context, RequireKnownRegion(regionId), solarPath, windPath),
                _ => PrintUsage(_error, 2),
            };
        }
        catch (Exception exception)
        {
            _error.WriteLine($"{OperationName(args)} failed: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Rejects a region argument that is not one of the five NEM regions. Region identity is a bare
    /// string throughout the pipeline, so a typo here would otherwise publish a
    /// <c>weather-{typo}.json</c> artifact that nothing ever reads.
    /// </summary>
    private static string RequireKnownRegion(string regionId) =>
        NemRegions.IsKnown(regionId)
            ? regionId
            : throw new ArgumentException(
                $"Region '{regionId}' is not a NEM region. Expected one of: "
                + $"{string.Join(", ", NemRegions.All.Order(StringComparer.Ordinal))}.");

    private int PrintVersion()
    {
        Assembly assembly = typeof(CommandRouter).Assembly;
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        _output.WriteLine($"NEM.CLI {version}");
        return 0;
    }

    private static int PrintUsage(TextWriter writer, int exitCode)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  NEM.CLI --help");
        writer.WriteLine("  NEM.CLI --version");
        writer.WriteLine();
        writer.WriteLine("  Scenario and sweep runs:");
        writer.WriteLine("  NEM.CLI --run-scenario [scenario-config.json]");
        writer.WriteLine("  NEM.CLI --fan-out-sweep <sweep-definition.json>");
        writer.WriteLine("  NEM.CLI --run-sweep <sweep-definition.json>");
        writer.WriteLine("  NEM.CLI --describe-schema <scenario|sweep>");
        writer.WriteLine();
        writer.WriteLine("  Input bundles:");
        writer.WriteLine("  NEM.CLI --validate-inputs [input-bundle]");
        writer.WriteLine("  NEM.CLI --ingest [input-bundle]");
        writer.WriteLine();
        writer.WriteLine("  Single-source imports (all covered by --ingest):");
        writer.WriteLine("  NEM.CLI --import-demand [output-directory]");
        writer.WriteLine("  NEM.CLI --generation-information <workbook.xlsx>");
        writer.WriteLine("  NEM.CLI --epw-report <region> <solar.epw> [wind.epw]");
        return exitCode;
    }

    private static string OperationName(string[] args) => args.FirstOrDefault() switch
    {
        "--run-scenario" => "Scenario run",
        "--fan-out-sweep" => "Sweep fan-out",
        "--run-sweep" => "Sweep run",
        "--describe-schema" => "Schema description",
        "--validate-inputs" => "Input validation",
        "--ingest" => "Input ingest",
        "--import-demand" => "Operational-demand import",
        "--generation-information" => "Generation-information import",
        "--epw-report" => "EPW report",
        _ => "Command",
    };
}
