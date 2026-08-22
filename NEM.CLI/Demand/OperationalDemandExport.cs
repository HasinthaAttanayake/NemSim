using NEM.Contracts;
using NEM.CLI.Infrastructure;
using NEM.Model.Series;

namespace NEM.CLI.Demand;

internal static class OperationalDemandExport
{
    public static ModelInputOutputDTO Create(OperationalDemandData demandData)
    {
        FlowSeries demand = demandData.Demand;
        var demandMegawatts = new double[demand.Length];
        for (int index = 0; index < demand.Length; index++)
        {
            demandMegawatts[index] = demand[index].Megawatts;
        }

        return new ModelInputOutputDTO(
            ArtifactSchemaVersions.OperationalDemand,
            new Scenario(
                $"{demandData.Region.ToLowerInvariant()}-operational-demand",
                demandData.Region,
                demand.Start,
                demand.Start.AddTicks(demand.Resolution.Ticks * demand.Length),
                demand.Resolution,
                "single region; no cross-region aggregation; identical overlaps deduplicated"),
            DateTimeOffset.UtcNow,
            new Sources(demandData.SourceArchives.ToArray()),
            new Series(demandMegawatts));
    }

    public static void WriteJson(ModelInputOutputDTO demandData, string path)
        => JsonFile.Write(demandData, path);
}