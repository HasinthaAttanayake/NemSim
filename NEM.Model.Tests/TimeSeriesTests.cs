using FluentAssertions;
using NEM.Model.Series;

namespace NemSim.Tests
{
    // TimeSeries is abstract, so its base invariants are exercised through FlowSeries,
    // the simplest concrete series. These tests cover construction and index-to-instant
    // behaviour shared by every series type, not flow-specific operations.
    public class TimeSeriesTests
    {
        private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);
        private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);

        private static DateTimeOffset NemStart => new(2026, 1, 1, 0, 0, 0, NemOffset);

        [Fact]
        public void Construction_Rejects_StartNotInNemTime()
        {
            var utcStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            var act = () => new FlowSeries(utcStart, HalfHour, new[] { 1.0, 2.0 });

            act.Should().Throw<ArgumentException>().WithMessage("*NEM market time*");
        }

        [Fact]
        public void Construction_Rejects_EmptyValues()
        {
            var act = () => new FlowSeries(NemStart, HalfHour, Array.Empty<double>());

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Construction_Rejects_NonFiniteValue(double bad)
        {
            var act = () => new FlowSeries(NemStart, HalfHour, new[] { 1.0, bad, 3.0 });

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Construction_Rejects_NonPositiveResolution()
        {
            var act = () => new FlowSeries(NemStart, TimeSpan.Zero, new[] { 1.0 });

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void InstantAt_MapsIndexToStartPlusIndexTimesResolution()
        {
            var series = new FlowSeries(NemStart, HalfHour, new[] { 10.0, 20.0, 30.0, 40.0 });

            var first = series.InstantAt(0);
            var fourth = series.InstantAt(3);

            first.Should().Be(NemStart);
            fourth.Should().Be(NemStart + TimeSpan.FromTicks(HalfHour.Ticks * 3));
        }

        [Fact]
        public void InstantAt_ConvertsEpwHourLabelToIntervalBeginning()
        {
            // EPW labels the interval end: "Hour 1 is 00:01 to 01:00", so hour h maps to index h-1.
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, NemOffset);
            const int epwHour = 1;
            var series = new FlowSeries(start, TimeSpan.FromHours(1), new[] { 10.0, 20.0 });

            var instant = series.InstantAt(epwHour - 1);

            instant.Should().Be(start);
        }

        [Fact]
        public void InstantAt_ConvertsAemoIntervalEndingLabelToIntervalBeginning()
        {
            // AEMO labels the interval end, so subtracting one resolution gives the start.
            var resolution = HalfHour;
            var firstLabelEnd = new DateTimeOffset(2026, 1, 1, 0, 30, 0, NemOffset);
            var start = firstLabelEnd - resolution;
            var series = new FlowSeries(start, resolution, new[] { 100.0, 200.0 });

            var firstInstant = series.InstantAt(0);

            firstInstant.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, NemOffset));
        }

        [Fact]
        public void Construction_PreservesOrderAndLength()
        {
            var values = new[] { 5.0, 1.0, 4.0, 1.0, 3.0 };

            var series = new FlowSeries(NemStart, HalfHour, values);

            series.Length.Should().Be(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                series[i].Megawatts.Should().Be(values[i]);
            }
        }

        [Fact]
        public void Construction_CopiesValues_SoLaterMutationDoesNotLeak()
        {
            var values = new[] { 1.0, 2.0, 3.0 };
            var series = new FlowSeries(NemStart, HalfHour, values);

            values[0] = 999.0;

            series[0].Megawatts.Should().Be(1.0);
        }
    }
}
