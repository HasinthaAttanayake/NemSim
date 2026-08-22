namespace NEM.CLI.Infrastructure;

internal sealed class RepositoryPaths
{
    private RepositoryPaths(string solutionRoot)
    {
        SolutionRoot = solutionRoot;
    }

    public string SolutionRoot { get; }
    public string WebDataDirectory => Path.Combine(SolutionRoot, "NEM.Web", "wwwroot", "data");
    public string DemandDataPath => WebDataPath("demand-data.json");
    public string DispatchResultsPath => WebDataPath("results.json");
    public string WeatherDataPath(string regionId) =>
        WebDataPath($"weather-{regionId.ToLowerInvariant()}.json");

    public static RepositoryPaths Discover(string startPath)
    {
        string candidate = Path.GetFullPath(startPath);
        for (int index = 0; index < 10; index++)
        {
            if (File.Exists(Path.Combine(candidate, "NemSim.slnx")))
            {
                return new RepositoryPaths(candidate);
            }

            candidate = Directory.GetParent(candidate)?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the NemSim solution root.");
        }

        throw new DirectoryNotFoundException("Could not locate the NemSim solution root.");
    }

    public string ResolveConfiguredPath(string path) =>
        Path.GetFullPath(path, SolutionRoot);

    public string WebDataPath(string fileName) =>
        Path.Combine(WebDataDirectory, fileName);
}