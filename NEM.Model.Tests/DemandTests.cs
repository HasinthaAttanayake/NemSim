using FluentAssertions;

namespace NEM.Model.Tests
{
    public class DemandTests
    {
        [Fact]
        public void Demand_ShouldSetProperties_WhenCreated()
        {
            // Arrange
            var time = DateTimeOffset.Now;
            var megawatts = 100.5;

            // Act
            var demand = new Demand(time, megawatts);

            // Assert
            demand.Time.Should().Be(time);
            demand.Megawatts.Should().Be(megawatts);
        }
    }
}
