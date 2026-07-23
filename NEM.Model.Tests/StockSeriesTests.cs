using FluentAssertions;
using NEM.Model.Series;

namespace NemSim.Tests
{
    public class StockSeriesTests
    {
        private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

        private static DateTimeOffset NemStart => new(2026, 1, 1, 0, 0, 0, NemOffset);

        // StockSeries deliberately exposes no combination or resample operators: a stock
        // cannot be summed as a flow because those methods do not exist. That guarantee is
        // structural, so it is checked by review, not by an assertion. This test pins only
        // what the type does expose.
        [Fact]
        public void Stock_ConstructsAndReadsStateOfCharge()
        {
            var charge = new[] { 120.0, 80.0, 40.0 };

            var soc = new StockSeries(NemStart, Hour, charge);

            soc.Length.Should().Be(3);
            soc[0].MegawattHours.Should().Be(120);
            soc[2].MegawattHours.Should().Be(40);
            soc.InstantAt(1).Should().Be(NemStart + Hour);
        }

        [Fact]
        public void Stock_Rejects_WhenStateOfChargeNegative()
        {
            var act = () => new StockSeries(NemStart, Hour, new[] { 120.0, -1.0, 40.0 });

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
