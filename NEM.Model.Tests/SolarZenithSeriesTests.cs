using FluentAssertions;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NemSim.Tests
{
    public class SolarZenithSeriesTests
    {
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
        private static DateTimeOffset Start =>
            new(2024, 6, 21, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Calculate_ReturnsTypedValuesCalculatedAtIntervalMidpoints()
        {
            SolarZenithSeries series = SolarZenithSeries.Calculate(
                Start, Hour, 24, -33.8688, 151.2093);
            SolarZenith expectedFirstValue = SolarZenith.At(
                -33.8688, 151.2093, Start + TimeSpan.FromMinutes(30));

            series[0].Should().Be(expectedFirstValue);
            series[0].Degrees.Should().BeInRange(0, 180);
            series.Length.Should().Be(24);
        }

        [Fact]
        public void Calculate_CarriesLocationAndIntervalBeginningTimeline()
        {
            SolarZenithSeries series = SolarZenithSeries.Calculate(
                Start, Hour, 24, -33.8688, 151.2093);

            series.Latitude.Should().Be(-33.8688);
            series.Longitude.Should().Be(151.2093);
            series.Start.Should().Be(Start);
            series.Resolution.Should().Be(Hour);
            series.InstantAt(1).Should().Be(Start + Hour);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Calculate_ShouldRejectNonPositiveLength(int length)
        {
            var act = () => SolarZenithSeries.Calculate(
                Start, Hour, length, -33.8688, 151.2093);

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("length");
        }
    }
}