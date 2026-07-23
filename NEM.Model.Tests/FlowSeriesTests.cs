using FluentAssertions;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NemSim.Tests
{
    public class FlowSeriesTests
    {
        private const double RelativeTolerance = 1e-12;
        private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);
        private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

        private static DateTimeOffset NemStart => new(2026, 1, 1, 0, 0, 0, NemOffset);

        private static double Tolerance(double expected) =>
            Math.Max(Math.Abs(expected) * RelativeTolerance, double.Epsilon);

        [Fact]
        public void Integrate_MultipliesEachValueByResolutionHours()
        {
            var flow = new FlowSeries(NemStart, HalfHour, new[] { 7400.0, 3000.0 });

            var energy = flow.Integrate();

            energy.MegawattHours.Should().BeApproximately(5200, Tolerance(5200));
        }

        [Fact]
        public void ResampleToHourly_TakesMeanOfEachHour()
        {
            var flow = new FlowSeries(NemStart, HalfHour, new[] { 400.0, 600.0 });

            var hourly = flow.ResampleToHourly();

            hourly.Resolution.Should().Be(Hour);
            hourly.Length.Should().Be(1);
            hourly[0].Megawatts.Should().BeApproximately(500, Tolerance(500));
        }

        [Fact]
        public void ResampleToHourly_PreservesEnergy()
        {
            var flow = new FlowSeries(NemStart, HalfHour, new[] { 400.0, 600.0 });

            var before = flow.Integrate().MegawattHours;
            var after = flow.ResampleToHourly().Integrate().MegawattHours;

            after.Should().BeApproximately(before, Tolerance(before));
        }

        [Fact]
        public void ResampleToHourly_TakesMeanFromFiveMinuteResolution()
        {
            var fiveMinutes = TimeSpan.FromMinutes(5);
            var values = new[] { 100.0, 200.0, 300.0, 400.0, 500.0, 600.0, 700.0, 800.0, 900.0, 1000.0, 1100.0, 1200.0 };
            var flow = new FlowSeries(NemStart, fiveMinutes, values);

            var hourly = flow.ResampleToHourly();

            hourly.Resolution.Should().Be(Hour);
            hourly.Length.Should().Be(1);
            hourly[0].Megawatts.Should().BeApproximately(650, Tolerance(650));
        }

        [Fact]
        public void ResampleToHourly_ReturnsSameInstance_WhenAlreadyHourly()
        {
            var flow = new FlowSeries(NemStart, Hour, new[] { 500.0, 600.0 });

            var hourly = flow.ResampleToHourly();

            hourly.Should().BeSameAs(flow);
        }

        [Fact]
        public void ResampleToHourly_Rejects_WhenLengthNotWholeHours()
        {
            var flow = new FlowSeries(NemStart, HalfHour, new[] { 400.0, 600.0, 500.0 });

            var act = () => flow.ResampleToHourly();

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void ResampleToHourly_Rejects_WhenResolutionDoesNotDivideHour()
        {
            var sevenMinutes = TimeSpan.FromMinutes(7);
            var flow = new FlowSeries(NemStart, sevenMinutes, new[] { 100.0, 200.0 });

            var act = () => flow.ResampleToHourly();

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void PositivePartAndNegativePart_RecombineToOriginal()
        {
            var residual = new FlowSeries(NemStart, HalfHour, new[] { 500.0, -300.0, 0.0, 120.5 });

            var deficit = residual.PositivePart();
            var surplus = residual.NegativePart();
            var recombined = deficit.Add(surplus);

            for (int i = 0; i < residual.Length; i++)
            {
                recombined[i].Megawatts.Should().BeApproximately(residual[i].Megawatts, Tolerance(residual[i].Megawatts));
            }

            deficit[1].Megawatts.Should().Be(0);
            surplus[0].Megawatts.Should().Be(0);
        }

        [Fact]
        public void ClampToCapacity_CapsValuesAboveCapacity()
        {
            var flow = new FlowSeries(NemStart, HalfHour, new[] { 200.0, 900.0, 500.0 });

            var clamped = flow.ClampToCapacity(Power.FromMegawatts(500));

            clamped[0].Megawatts.Should().Be(200);
            clamped[1].Megawatts.Should().Be(500);
            clamped[2].Megawatts.Should().Be(500);
        }

        [Fact]
        public void Scale_MultipliesEveryValue()
        {
            var flow = new FlowSeries(NemStart, HalfHour, new[] { 100.0, 250.0 });

            var scaled = flow.Scale(1.5);

            scaled[0].Megawatts.Should().BeApproximately(150, Tolerance(150));
            scaled[1].Megawatts.Should().BeApproximately(375, Tolerance(375));
        }

        [Fact]
        public void AddAndSubtract_OperateElementWise_WhenAligned()
        {
            var demand = new FlowSeries(NemStart, HalfHour, new[] { 6000.0, 5500.0 });
            var generation = new FlowSeries(NemStart, HalfHour, new[] { 6500.0, 5000.0 });

            var sum = demand.Add(generation);
            var residual = demand.Subtract(generation);

            sum[0].Megawatts.Should().BeApproximately(12500, Tolerance(12500));
            residual[0].Megawatts.Should().BeApproximately(-500, Tolerance(500));
            residual[1].Megawatts.Should().BeApproximately(500, Tolerance(500));
        }

        public enum Mismatch { Start, Resolution, Length }

        [Theory]
        [InlineData(true, Mismatch.Start, "*misaligned on start*")]
        [InlineData(true, Mismatch.Resolution, "*misaligned on resolution*")]
        [InlineData(true, Mismatch.Length, "*misaligned on length*")]
        [InlineData(false, Mismatch.Start, "*misaligned on start*")]
        [InlineData(false, Mismatch.Resolution, "*misaligned on resolution*")]
        [InlineData(false, Mismatch.Length, "*misaligned on length*")]
        public void AddAndSubtract_Reject_OnEveryMismatch(bool add, Mismatch mismatch, string message)
        {
            var a = new FlowSeries(NemStart, HalfHour, new[] { 1.0, 2.0 });
            var b = mismatch switch
            {
                Mismatch.Start => new FlowSeries(NemStart + HalfHour, HalfHour, new[] { 1.0, 2.0 }),
                Mismatch.Resolution => new FlowSeries(NemStart, Hour, new[] { 1.0, 2.0 }),
                Mismatch.Length => new FlowSeries(NemStart, HalfHour, new[] { 1.0, 2.0, 3.0 }),
                _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
            };

            var act = () => add ? a.Add(b) : a.Subtract(b);

            act.Should().Throw<ArgumentException>().WithMessage(message);
        }
    }
}
