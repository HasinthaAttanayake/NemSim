using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NEM.Contracts;

public sealed class WeatherDataDTO
{
    public WeatherDataDTO(
        int schemaVersion,
        string sourceFile,
        WeatherLocation location,
        DateTimeOffset start,
        TimeSpan resolution,
        double windMeasurementHeightMetres,
        WeatherSeriesData dataSeries)
    {
        SchemaVersion = schemaVersion;
        SourceFile = sourceFile;
        Location = location;
        Start = start;
        Resolution = resolution;
        WindMeasurementHeightMetres = windMeasurementHeightMetres;
        DataSeries = dataSeries;
    }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("sourceFile")]
    [Required]
    public string SourceFile { get; set; }

    [JsonPropertyName("location")]
    [Required]
    public WeatherLocation Location { get; set; }

    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; set; }

    [JsonPropertyName("resolution")]
    public TimeSpan Resolution { get; set; }

    [JsonPropertyName("windMeasurementHeightMetres")]
    public double WindMeasurementHeightMetres { get; set; }

    [JsonPropertyName("dataSeries")]
    [Required]
    public WeatherSeriesData DataSeries { get; set; }
}

public readonly record struct WeatherLocation(
    string City,
    string Wmo,
    double Latitude,
    double Longitude);

public readonly record struct WeatherSeriesData(
    double[] GlobalHorizontalRadiationWhPerSquareMetre,
    double[] DirectNormalRadiationWhPerSquareMetre,
    double[] DiffuseHorizontalRadiationWhPerSquareMetre,
    double[] SolarZenithDegrees,
    double[] WindSpeedMetresPerSecond);