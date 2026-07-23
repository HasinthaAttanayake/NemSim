using NEM.Contracts;
using System.Text.Json;

namespace NEM.CLI;

internal static class EpwWeatherExport
{
    public static WeatherDataDTO Create(
        EpwHeader header,
        EpwWeatherSeries weather,
        string sourceFile)
    {
        double windMeasurementHeightMetres = weather.WindSpeed.MeasurementHeightMetres
            ?? throw new InvalidOperationException(
                "Weather export requires a wind-speed trace with a measurement height.");

        return new WeatherDataDTO(
            1,
            sourceFile,
            new WeatherLocation(
                header.City,
                header.Wmo,
                header.Latitude,
                header.Longitude),
            weather.DirectNormalRadiation.Start,
            weather.DirectNormalRadiation.Resolution,
            windMeasurementHeightMetres,
            new WeatherSeriesData(
                ValuesOf(weather.DirectNormalRadiation),
                ValuesOf(weather.WindSpeed)));
    }

    public static void WriteJson(WeatherDataDTO weatherData, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(weatherData, options));
    }

    private static double[] ValuesOf(NEM.Model.Series.TraceSeries series)
    {
        var values = new double[series.Length];
        for (int index = 0; index < series.Length; index++)
        {
            values[index] = series[index];
        }

        return values;
    }
}