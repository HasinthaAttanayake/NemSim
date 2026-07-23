using FluentAssertions;
using NEM.Model.Units;

namespace NemSim.Tests
{
    public class UnitsTests
    {
        // Numeric assertions use an explicit tolerance because double equality is exact and mishandles NaN.
        private const double Tolerance = 1e-9;

        [Fact]
        public void EnergyFrom_ComputesMegawattHours_WhenPowerAveragedOverHalfHour()
        {
            var power = Power.FromMegawatts(7400);

            var energy = Energy.From(power, TimeSpan.FromMinutes(30));

            energy.MegawattHours.Should().BeApproximately(3700, Tolerance);
        }

        [Fact]
        public void EnergyFrom_EqualsPowerNumerically_WhenIntervalIsOneHour()
        {
            // At hourly resolution MW and MWh are numerically identical, which makes conflating them easy to miss.
            var power = Power.FromMegawatts(7400);

            var energy = Energy.From(power, TimeSpan.FromHours(1));

            energy.MegawattHours.Should().BeApproximately(power.Megawatts, Tolerance);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void PowerFromMegawatts_ShouldReject_WhenNotFinite(double value)
        {
            var act = () => Power.FromMegawatts(value);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void EnergyFromMegawattHours_ShouldReject_WhenNotFinite(double value)
        {
            var act = () => Energy.FromMegawattHours(value);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void PowerFromMegawatts_AcceptsNegative_BecauseFlowsAreSigned()
        {
            var power = Power.FromMegawatts(-500);

            power.Megawatts.Should().BeApproximately(-500, Tolerance);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-30)]
        public void EnergyFrom_ShouldReject_WhenIntervalNotPositive(int minutes)
        {
            var power = Power.FromMegawatts(500);

            var act = () => Energy.From(power, TimeSpan.FromMinutes(minutes));

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void PowerSum_ShouldAggregateNameplateCapacities()
        {
            var fleet = new[]
            {
                Power.FromMegawatts(700),
                Power.FromMegawatts(1200),
                Power.FromMegawatts(150),
            };

            fleet.Sum().Megawatts.Should().BeApproximately(2050, Tolerance);
        }

        [Fact]
        public void PowerSum_ShouldBeZero_WhenEmpty()
        {
            var empty = Array.Empty<Power>();

            empty.Sum().Should().Be(Power.Zero);
        }

        [Fact]
        public void EnergySum_ShouldAggregateAcrossTechnologies()
        {
            var totals = new[]
            {
                Energy.FromMegawattHours(3700),
                Energy.FromMegawattHours(900),
            };

            totals.Sum().MegawattHours.Should().BeApproximately(4600, Tolerance);
        }

        [Fact]
        public void PowerSubtraction_ShouldProduceSignedResidualDemand()
        {
            var demand = Power.FromMegawatts(6000);
            var generation = Power.FromMegawatts(6500);

            var residual = demand - generation;

            residual.Megawatts.Should().BeApproximately(-500, Tolerance);
        }

        [Fact]
        public void PowerScaling_ShouldMultiplyByFactor()
        {
            var scaled = Power.FromMegawatts(400) * 1.5;

            scaled.Megawatts.Should().BeApproximately(600, Tolerance);
        }

        [Fact]
        public void EnergyDividedByInterval_ShouldGiveAveragePower()
        {
            var power = Energy.FromMegawattHours(3700) / TimeSpan.FromMinutes(30);

            power.Megawatts.Should().BeApproximately(7400, Tolerance);
        }

        [Fact]
        public void EnergyDividedByPower_ShouldGiveStorageDuration()
        {
            var duration = Energy.FromMegawattHours(500) / Power.FromMegawatts(250);

            duration.TotalHours.Should().BeApproximately(2, Tolerance);
        }

        [Fact]
        public void EnergyDividedByPower_ShouldBeZero_WhenBothZero()
        {
            var duration = Energy.Zero / Power.Zero;

            duration.Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void EnergyDividedByPower_ShouldThrow_WhenPowerZeroButEnergyNot()
        {
            var act = () => Energy.FromMegawattHours(100) / Power.Zero;

            act.Should().Throw<DivideByZeroException>();
        }

        [Fact]
        public void EnergyDividedByEnergy_ShouldGiveDimensionlessShare()
        {
            var share = Energy.FromMegawattHours(3000) / Energy.FromMegawattHours(12000);

            share.Should().BeApproximately(0.25, Tolerance);
        }

        [Fact]
        public void EnergyDividedByEnergy_ShouldThrow_WhenDenominatorZero()
        {
            var act = () => Energy.FromMegawattHours(100) / Energy.Zero;

            act.Should().Throw<DivideByZeroException>();
        }

        [Fact]
        public void PowerComparison_ShouldSupportMinAndOrdering()
        {
            var residual = Power.FromMegawatts(800);
            var available = Power.FromMegawatts(500);

            (residual > available).Should().BeTrue();
            Power.Min(residual, available).Should().Be(available);
            Power.Max(residual, available).Should().Be(residual);
        }

        [Fact]
        public void PowerTimesTimeSpan_ShouldGiveEnergy()
        {
            var energy = Power.FromMegawatts(7400) * TimeSpan.FromMinutes(30);

            energy.MegawattHours.Should().BeApproximately(3700, Tolerance);
        }

        [Fact]
        public void EnergyComparison_ShouldSupportMinAndMax()
        {
            var a = Energy.FromMegawattHours(3000);
            var b = Energy.FromMegawattHours(5000);

            Energy.Min(a, b).Should().Be(a);
            Energy.Max(a, b).Should().Be(b);
        }

        [Fact]
        public void EnergyDividedByPower_ShouldThrow_WhenDurationWouldBeNegative()
        {
            var act = () => Energy.FromMegawattHours(-100) / Power.FromMegawatts(50);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void EnergyDividedByPower_Throws_WhenPowerRatingNegative()
        {
            var act = () => Energy.FromMegawattHours(-100) / Power.FromMegawatts(-50);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
