using FluentAssertions;
using NEM.Contracts;
using NEM.Model.Series;
using System.Text.Json;

namespace NEM.CLI.Tests;

public sealed class EpwWeatherExportTests
{
    [Fact]
    public void Create_RoundTripsLocationAndSeries_ThroughJson()
    {
        var header = new EpwHeader(
            "Sydney Observatory Hill",
            "947680",
            -33.8608,
            151.205,
            10,
            false,
            1,
            1,
            9);
        var weather = new EpwWeatherSeries(
            TraceSeries.GlobalHorizontalRadiation(
                EpwParser.SyntheticNonLeapStart,
                TimeSpan.FromHours(1),
                [0, 500, 900]),
            TraceSeries.DirectNormalRadiation(
                EpwParser.SyntheticNonLeapStart,
                TimeSpan.FromHours(1),
                [0, 420, 760]),
            TraceSeries.DiffuseHorizontalRadiation(
                EpwParser.SyntheticNonLeapStart,
                TimeSpan.FromHours(1),
                [0, 80, 140]),
            SolarZenithSeries.Calculate(
                EpwParser.SyntheticNonLeapStart,
                TimeSpan.FromHours(1),
                3,
                -33.8608,
                151.205),
            TraceSeries.WindSpeed(
                EpwParser.SyntheticNonLeapStart,
                TimeSpan.FromHours(1),
                [4.2, 6.8, 5.1],
                10));

        WeatherDataDTO export = EpwWeatherExport.Create(header, weather, "sydney.epw");
        string json = JsonSerializer.Serialize(export);
        WeatherDataDTO? roundTripped = JsonSerializer.Deserialize<WeatherDataDTO>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.SchemaVersion.Should().Be(2);
        roundTripped.SourceFile.Should().Be("sydney.epw");
        roundTripped.Location.City.Should().Be("Sydney Observatory Hill");
        roundTripped.Location.Wmo.Should().Be("947680");
        roundTripped.Start.Should().Be(EpwParser.SyntheticNonLeapStart);
        roundTripped.Resolution.Should().Be(TimeSpan.FromHours(1));
        roundTripped.WindMeasurementHeightMetres.Should().Be(10);
        roundTripped.DataSeries.GlobalHorizontalRadiationWhPerSquareMetre.Should().Equal(0, 500, 900);
        roundTripped.DataSeries.DirectNormalRadiationWhPerSquareMetre.Should().Equal(0, 420, 760);
        roundTripped.DataSeries.DiffuseHorizontalRadiationWhPerSquareMetre.Should().Equal(0, 80, 140);
        roundTripped.DataSeries.SolarZenithDegrees.Should().Equal(
            Enumerable.Range(0, weather.SolarZenith.Length)
                .Select(index => weather.SolarZenith[index].Degrees));
        roundTripped.DataSeries.WindSpeedMetresPerSecond.Should().Equal(4.2, 6.8, 5.1);
    }

    [Fact]
    public void Create_ThrowsClearMessage_WhenWindTraceHasNoMeasurementHeight()
    {
        var header = new EpwHeader(
            "Sydney Observatory Hill",
            "947680",
            -33.8608,
            151.205,
            10,
            false,
            1,
            1,
            9);
        TraceSeries radiation = TraceSeries.DirectNormalRadiation(
            EpwParser.SyntheticNonLeapStart,
            TimeSpan.FromHours(1),
            [0, 420, 760]);
        SolarZenithSeries solarZenith = SolarZenithSeries.Calculate(
            EpwParser.SyntheticNonLeapStart,
            TimeSpan.FromHours(1),
            3,
            -33.8608,
            151.205);
        var weather = new EpwWeatherSeries(
            radiation, radiation, radiation, solarZenith, radiation);

        var act = () => EpwWeatherExport.Create(header, weather, "sydney.epw");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*wind-speed trace with a measurement height*");
    }
}