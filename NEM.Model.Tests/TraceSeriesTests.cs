using FluentAssertions;
using NEM.Model.Series;

namespace NemSim.Tests
{
    public class TraceSeriesTests
    {
        private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

        private static DateTimeOffset NemStart => new(2026, 1, 1, 0, 0, 0, NemOffset);

        [Fact]
        public void WindSpeedTrace_CarriesHeightAndUnit()
        {
            var trace = TraceSeries.WindSpeed(NemStart, Hour, new[] { 5.0, 7.5, 6.0 }, measurementHeightMetres: 10);

            trace.Unit.Should().Be(TraceUnit.MetresPerSecond);
            trace.MeasurementHeightMetres.Should().Be(10);
            trace[1].Should().Be(7.5);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        [InlineData(double.NaN)]
        public void WindSpeedTrace_Rejects_WhenHeightNotPositive(double height)
        {
            var act = () => TraceSeries.WindSpeed(NemStart, Hour, new[] { 5.0 }, height);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void DirectNormalRadiationTrace_HasNoHeightAndWattHourUnit()
        {
            var trace = TraceSeries.DirectNormalRadiation(NemStart, Hour, new[] { 0.0, 350.0, 800.0 });

            trace.Unit.Should().Be(TraceUnit.DirectNormalRadiationWattHoursPerSquareMetre);
            trace.MeasurementHeightMetres.Should().BeNull();
            trace[2].Should().Be(800);
        }

        [Theory]
        [MemberData(nameof(NonWindTraceCases))]
        public void NonWindTrace_CarriesDistinctUnitAndNoHeight(
            TraceSeries trace,
            TraceUnit expectedUnit,
            double expectedValue)
        {
            trace.Unit.Should().Be(expectedUnit);
            trace.MeasurementHeightMetres.Should().BeNull();
            trace[1].Should().Be(expectedValue);
        }

        public static TheoryData<TraceSeries, TraceUnit, double> NonWindTraceCases => new()
        {
            {
                TraceSeries.GlobalHorizontalRadiation(NemStart, Hour, [0, 650]),
                TraceUnit.GlobalHorizontalRadiationWattHoursPerSquareMetre,
                650
            },
            {
                TraceSeries.DiffuseHorizontalRadiation(NemStart, Hour, [0, 125]),
                TraceUnit.DiffuseHorizontalRadiationWattHoursPerSquareMetre,
                125
            },
        };
    }
}
