using FluentAssertions;
using NEM.Model.Units;

namespace NemSim.Tests
{
    public class SolarZenithTests
    {
        private const double AngleTolerance = 1e-9;
        private const double OnlineCalculatorAngleTolerance = 0.5;

        [Theory]
        [InlineData(0, 0, 2024, 3, 20, 12, 0, 0, 0, 1.9868614158735136)]
        [InlineData(-33.8688, 151.2093, 2024, 12, 21, 13, 0, 0, 11, 10.5590839160866)]
        [InlineData(40.7128, -74.0060, 2024, 6, 21, 12, 0, 0, -4, 21.05570621489425)]
        public void SolarZenithAngle_ShouldMatchNoaaEquations(
            double latitude,
            double longitude,
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            double utcOffsetHours,
            double expectedAngle)
        {
            var time = new DateTimeOffset(
                year, month, day, hour, minute, second,
                TimeSpan.FromHours(utcOffsetHours));
            SolarZenith solarZenith = SolarZenith.At(latitude, longitude, time);

            double angle = solarZenith.Degrees;

            angle.Should().BeApproximately(expectedAngle, AngleTolerance);
        }

        [Fact]
        public void SolarZenithAngle_ShouldAgreeWithinHalfDegreeOfNoaaOnlineCalculator_ForBoulderOn2024April20()
        {
            // The legacy NOAA calculator uses west-positive longitude and UTC offset.
            // Its displayed cos(zenith) is 0.881, which is acos(0.881) = 28.2368 degrees.
            // It uses a different model than NOAA's published fractional-year equations.
            var time = new DateTimeOffset(2024, 4, 20, 12, 0, 0, TimeSpan.FromHours(-7));
            SolarZenith solarZenith = SolarZenith.At(40.125, -105.23694444444445, time);

            double angle = solarZenith.Degrees;

            angle.Should().BeApproximately(28.2367709171764, OnlineCalculatorAngleTolerance);
        }

        [Fact]
        public void SolarZenithAngle_ShouldUse366Days_DuringLeapYear()
        {
            var time = new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.Zero);
            SolarZenith solarZenith = SolarZenith.At(0, 0, time);

            double angle = solarZenith.Degrees;

            angle.Should().BeApproximately(8.569363051881247, AngleTolerance);
        }

        [Theory]
        [InlineData(-90, -180)]
        [InlineData(90, 180)]
        public void At_ShouldAcceptCoordinateBoundaries(
            double latitude,
            double longitude)
        {
            var time = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

            SolarZenith solarZenith = SolarZenith.At(latitude, longitude, time);

            solarZenith.Degrees.Should().BeInRange(0, 180);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-90.0001)]
        [InlineData(90.0001)]
        public void At_ShouldRejectInvalidLatitude(double latitude)
        {
            var time = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var act = () => SolarZenith.At(latitude, 0, time);

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("latitude");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-180.0001)]
        [InlineData(180.0001)]
        public void At_ShouldRejectInvalidLongitude(double longitude)
        {
            var time = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

            var act = () => SolarZenith.At(0, longitude, time);

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("longitude");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-0.0001)]
        [InlineData(180.0001)]
        public void FromDegrees_ShouldRejectInvalidAngle(double degrees)
        {
            var act = () => SolarZenith.FromDegrees(degrees);

            act.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("degrees");
        }
    }
}