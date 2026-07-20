using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NEM.Contracts
{
    public class ModelInputOutputDTO
    {
        public ModelInputOutputDTO(int schemaVersion, Scenario scenario, DateTimeOffset generatedAt, Sources dataSources, Series dataSeries)
        {
            SchemaVersion = schemaVersion;
            Scenario = scenario;
            GeneratedAt = generatedAt;
            DataSources = dataSources;
            DataSeries = dataSeries;
        }

        [JsonPropertyName("schemaVersion")]
        [Required]
        public int SchemaVersion { get; set; }
        [JsonPropertyName("scenario")]
        [Required]
        public Scenario Scenario { get; set; }
        [JsonPropertyName("generatedAt")]
        [Required]
        public DateTimeOffset GeneratedAt { get; set; }
        [JsonPropertyName("dataSources")]
        [Required]
        public Sources DataSources { get; set; }
        [JsonPropertyName("dataSeries")]
        [Required]
        public Series DataSeries { get; set; }
    }

    public struct Sources(string[] demandSourceFiles)
    {
        public string[] DemandSourceFiles { get; set; } = demandSourceFiles;
    }

    public struct Series(double[] demandMw)
    {
        public double[] DemandMw { get; set; } = demandMw;
    }

    public struct Scenario(string id, string region, DateTimeOffset periodStart, DateTimeOffset periodEnd, TimeSpan resolution, string aggregation)
    {
        public string Id { get; set; } = id;
        public string Region { get; set; } = region;
        public DateTimeOffset PeriodStart { get; set; } = periodStart;
        public DateTimeOffset PeriodEnd { get; set; } = periodEnd;
        public TimeSpan Resolution { get; set; } = resolution;
        public string Aggregation { get; set; } = aggregation;
    }
}
