using NEM.CLI.Application;
using NEM.CLI.Infrastructure;
using NEM.Contracts;
using NEM.Model.Weather;

namespace NEM.CLI.Weather;

internal static class EpwCommands
{
    public static int WriteReport(
        CliContext context,
        string regionId,
        string solarSourcePath,
        string? windSourcePath = null)
    {
        windSourcePath ??= solarSourcePath;
        EpwFile solarEpw = EpwParser.ReadValidated(solarSourcePath);
        EpwFile windEpw = string.Equals(solarSourcePath, windSourcePath, StringComparison.OrdinalIgnoreCase)
            ? solarEpw
            : EpwParser.ReadValidated(windSourcePath);
        RegionalResourceProfile solarWeather = EpwParser.ReadTimeSeries(solarEpw);
        RegionalResourceProfile windWeather = ReferenceEquals(solarEpw, windEpw)
            ? solarWeather
            : EpwParser.ReadTimeSeries(windEpw);
        EpwProvenanceReport report = EpwParser.ReadProvenance(solarEpw);
        WeatherDataDTO weatherData = EpwWeatherExport.Create(
            regionId,
            solarEpw.Header,
            solarWeather,
            Path.GetFileName(solarSourcePath),
            windEpw.Header,
            windWeather,
            Path.GetFileName(windSourcePath));
        EpwWeatherExport.WriteJson(weatherData, context.Paths.WeatherDataPath(regionId));
        context.Output.WriteLine(JsonFile.SerializeReadable(report));
        context.Output.WriteLine(
            $"Daylight DNI shares total: {report.DaylightDniSourceShares.Values.Sum():F2}%");
        context.Output.WriteLine(
            $"Constructed {solarWeather.GlobalHorizontalRadiation.Length} GHI, "
            + $"{solarWeather.DirectNormalRadiation.Length} DNI, "
            + $"{solarWeather.DiffuseHorizontalRadiation.Length} DHI, "
            + $"{solarWeather.SolarZenith.Length} solar zenith, "
            + $"{solarWeather.DryBulbTemperature.Length} dry-bulb temperature, and "
            + $"{windWeather.WindSpeed.Length} wind values.");
        context.Output.WriteLine(
            $"Wrote weather data to: {Path.GetFullPath(context.Paths.WeatherDataPath(regionId))}");
        return 0;
    }

}