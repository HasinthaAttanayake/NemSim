using NEM.Model.Units;

namespace NEM.Model.Series
{
    /// <summary>
    /// Calculated geometric solar zenith angles for aligned intervals. Each value is
    /// calculated at the interval midpoint so it represents the same period as an
    /// interval-integrated EPW radiation value.
    /// </summary>
    public sealed class SolarZenithSeries : TimeSeries
    {
        private SolarZenithSeries(
            DateTimeOffset start,
            TimeSpan resolution,
            double[] degrees,
            double latitude,
            double longitude)
            : base(start, resolution, degrees)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude { get; }

        public double Longitude { get; }

        public SolarZenith this[int index] => SolarZenith.FromDegrees(RawValue(index));

        public static SolarZenithSeries Calculate(
            DateTimeOffset start,
            TimeSpan resolution,
            int length,
            double latitude,
            double longitude)
        {
            NemTime.Require(start, nameof(start));

            if (resolution <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolution), resolution, "Resolution must be positive.");
            }

            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length), length, "Length must be positive.");
            }

            var values = new double[length];
            TimeSpan midpointOffset = TimeSpan.FromTicks(resolution.Ticks / 2);
            for (int index = 0; index < length; index++)
            {
                DateTimeOffset midpoint = start
                    + TimeSpan.FromTicks(resolution.Ticks * index)
                    + midpointOffset;
                values[index] = SolarZenith.At(latitude, longitude, midpoint).Degrees;
            }

            return new SolarZenithSeries(
                start, resolution, values, latitude, longitude);
        }
    }
}