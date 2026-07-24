using System.Globalization;
using System.Runtime.CompilerServices;

namespace NEM.Model.Units
{
    /// <summary>
    /// Power in megawatts (MW) — a rate, not a quantity of energy.
    /// <para>
    /// A power figure is either an average over an interval (e.g. metered demand
    /// over a block) or a capacity (e.g. a generator's nameplate rating). To get
    /// energy you must multiply by a duration; see <see cref="Energy.From"/>.
    /// When the power is an interval average, energy = power × duration is exact.
    /// </para>
    /// <para>
    /// Power is signed (e.g. an interconnector reversing direction, or residual
    /// demand net of generation), so negatives are allowed; only NaN and infinity
    /// are rejected.
    /// </para>
    /// <para>
    /// Adding two powers is meaningful across space (summing a fleet at the same
    /// instant), but not across time: two consecutive interval averages do not add
    /// to the average over the combined interval — for that you sum energy, not
    /// power. The operator here does not distinguish the two; combining across time
    /// is guarded where a series and its resolution are known, not on the scalar.
    /// </para>
    /// </summary>
    public readonly record struct Power : IComparable<Power>
    {
        public double Megawatts { get; }

        private Power(double megawatts) => Megawatts = megawatts;

        /// <summary>Zero power. Seed for summing a collection of powers.</summary>
        public static Power Zero { get; } = new(0);

        /// <summary>
        /// Creates a <see cref="Power"/> from a value in megawatts. Naming the unit
        /// in the factory keeps the unit explicit at the call site.
        /// </summary>
        public static Power FromMegawatts(double megawatts)
        {
            if (double.IsNaN(megawatts) || double.IsInfinity(megawatts))
            {
                throw new ArgumentException(
                    "Power must be a finite number.",
                    nameof(megawatts));
            }

            return new Power(megawatts);
        }

        public static Power operator +(Power a, Power b)
            => FromMegawatts(a.Megawatts + b.Megawatts);

        public static Power operator -(Power a, Power b)
            => FromMegawatts(a.Megawatts - b.Megawatts);

        public static Power operator *(Power power, double factor)
            => FromMegawatts(power.Megawatts * factor);

        public static Power operator *(double factor, Power power)
            => FromMegawatts(power.Megawatts * factor);

        /// <summary>Energy from this power sustained over <paramref name="interval"/>.</summary>
        public static Energy operator *(Power power, TimeSpan interval)
            => Energy.From(power, interval);

        public static bool operator <(Power a, Power b) => a.Megawatts < b.Megawatts;
        public static bool operator >(Power a, Power b) => a.Megawatts > b.Megawatts;
        public static bool operator <=(Power a, Power b) => a.Megawatts <= b.Megawatts;
        public static bool operator >=(Power a, Power b) => a.Megawatts >= b.Megawatts;

        public int CompareTo(Power other) => Megawatts.CompareTo(other.Megawatts);

        public static Power Min(Power a, Power b) => a.Megawatts <= b.Megawatts ? a : b;
        public static Power Max(Power a, Power b) => a.Megawatts >= b.Megawatts ? a : b;
    }

    /// <summary>
    /// Energy in megawatt-hours (MWh).
    /// <para>
    /// Interval energy (net import/export over a period) is signed, so negatives
    /// are allowed here; only NaN and infinity are rejected. Where energy is a
    /// stored level (state of charge) it cannot go below zero, but that constraint
    /// is enforced where storage state is tracked, not on this scalar.
    /// </para>
    /// <para>
    /// Energy sums across both space and time, so <c>+</c> is defined.
    /// </para>
    /// </summary>
    public readonly record struct Energy : IComparable<Energy>
    {
        public double MegawattHours { get; }

        private Energy(double megawattHours) => MegawattHours = megawattHours;

        /// <summary>Zero energy. Seed for summing a collection of energies.</summary>
        public static Energy Zero { get; } = new(0);

        /// <summary>
        /// Creates an <see cref="Energy"/> from a value in megawatt-hours.
        /// </summary>
        public static Energy FromMegawattHours(double megawattHours)
        {
            if (double.IsNaN(megawattHours) || double.IsInfinity(megawattHours))
            {
                throw new ArgumentException(
                    "Energy must be a finite number.",
                    nameof(megawattHours));
            }

            return new Energy(megawattHours);
        }

        /// <summary>
        /// Energy from average <paramref name="power"/> sustained over
        /// <paramref name="interval"/>: MWh = MW × hours. The interval is required
        /// and must be positive — a duration cannot be zero or run backwards.
        /// </summary>
        /// <param name="power">Average power over the interval (MW).</param>
        /// <param name="interval">Duration the average applies to; must be positive.</param>
        public static Energy From(Power power, TimeSpan interval)
        {
            RequirePositive(interval, nameof(interval));
            return FromMegawattHours(power.Megawatts * interval.TotalHours);
        }

        public static Energy operator +(Energy a, Energy b)
            => FromMegawattHours(a.MegawattHours + b.MegawattHours);

        public static Energy operator -(Energy a, Energy b)
            => FromMegawattHours(a.MegawattHours - b.MegawattHours);

        public static Energy operator *(Energy energy, double factor)
            => FromMegawattHours(energy.MegawattHours * factor);

        public static Energy operator *(double factor, Energy energy)
            => FromMegawattHours(energy.MegawattHours * factor);

        /// <summary>Average power over <paramref name="interval"/>: MW = MWh ÷ hours.</summary>
        public static Power operator /(Energy energy, TimeSpan interval)
        {
            RequirePositive(interval, nameof(interval));
            return Power.FromMegawatts(energy.MegawattHours / interval.TotalHours);
        }

        /// <summary>
        /// Duration this energy would last at <paramref name="power"/> (e.g. storage
        /// duration): hours = MWh ÷ MW. The power rating must be non-negative, and a
        /// zero rating is only valid when the energy is also zero, in which case the
        /// duration is zero.
        /// </summary>
        public static TimeSpan operator /(Energy energy, Power power)
        {
            if (power.Megawatts < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(power), power.Megawatts,
                    "A power rating cannot be negative when deriving a duration.");
            }

            if (power.Megawatts == 0)
            {
                if (energy.MegawattHours == 0)
                {
                    return TimeSpan.Zero;
                }

                throw new DivideByZeroException(
                    "Cannot derive a duration from non-zero energy at zero power.");
            }

            double hours = energy.MegawattHours / power.Megawatts;
            if (hours < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(energy), energy.MegawattHours,
                    "A storage duration cannot be negative; energy must be non-negative here.");
            }

            return TimeSpan.FromHours(hours);
        }

        /// <summary>Dimensionless ratio (e.g. renewable share, capacity factor).</summary>
        public static double operator /(Energy numerator, Energy denominator)
        {
            if (denominator.MegawattHours == 0)
            {
                throw new DivideByZeroException("Cannot divide energy by zero energy.");
            }

            return numerator.MegawattHours / denominator.MegawattHours;
        }

        public static bool operator <(Energy a, Energy b) => a.MegawattHours < b.MegawattHours;
        public static bool operator >(Energy a, Energy b) => a.MegawattHours > b.MegawattHours;
        public static bool operator <=(Energy a, Energy b) => a.MegawattHours <= b.MegawattHours;
        public static bool operator >=(Energy a, Energy b) => a.MegawattHours >= b.MegawattHours;

        public int CompareTo(Energy other) => MegawattHours.CompareTo(other.MegawattHours);

        public static Energy Min(Energy a, Energy b) => a.MegawattHours <= b.MegawattHours ? a : b;
        public static Energy Max(Energy a, Energy b) => a.MegawattHours >= b.MegawattHours ? a : b;

        private static void RequirePositive(TimeSpan interval, string paramName)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    paramName, interval, "Interval must be positive.");
            }
        }
    }

    /// <summary>
    /// Aggregation over quantity collections. These types have no LINQ
    /// <c>Sum()</c>, so summing seeds from <c>Zero</c>.
    /// </summary>
    public static class QuantityExtensions
    {
        public static Power Sum(this IEnumerable<Power> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var total = Power.Zero;
            foreach (var power in source)
            {
                total += power;
            }

            return total;
        }

        public static Energy Sum(this IEnumerable<Energy> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var total = Energy.Zero;
            foreach (var energy in source)
            {
                total += energy;
            }

            return total;
        }
    }

    /// <summary>
    /// Geometric solar zenith angle in degrees. Zero degrees is directly overhead
    /// and 90 degrees is the astronomical horizon.
    /// </summary>
    public readonly record struct SolarZenith : IComparable<SolarZenith>
    {
        public double Degrees { get; }

        private SolarZenith(double degrees)
        {
            Degrees = degrees;
        }

        public static SolarZenith FromDegrees(double degrees)
        {
            if (!double.IsFinite(degrees) || degrees is < 0 or > 180)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(degrees), degrees,
                    "Solar zenith must be a finite value between 0 and 180 degrees.");
            }

            return new SolarZenith(degrees);
        }

        /// <summary>
        /// Calculates the geometric solar zenith angle for a location and instant
        /// using NOAA's General Solar Position Calculations equations.
        /// </summary>
        public static SolarZenith At(double latitude, double longitude, DateTimeOffset timeOfDay)
        {
            if (!double.IsFinite(latitude) || latitude < -90 || latitude > 90)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(latitude), latitude,
                    "Latitude must be a finite value between -90 and +90 degrees.");
            }

            if (!double.IsFinite(longitude) || longitude < -180 || longitude > 180)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(longitude), longitude,
                    "Longitude must be a finite value between -180 and +180 degrees.");
            }

            int daysInYear = DateTime.IsLeapYear(timeOfDay.Year) ? 366 : 365;
            double fractionalHour = timeOfDay.Hour
                + timeOfDay.Minute / 60.0
                + timeOfDay.Second / 3600.0
                + timeOfDay.Millisecond / 3_600_000.0;
            double fractionalYear = 2 * Math.PI / daysInYear
                * (timeOfDay.DayOfYear - 1 + (fractionalHour - 12) / 24);

            double equationOfTime = 229.18 * (
                0.000075
                + 0.001868 * Math.Cos(fractionalYear)
                - 0.032077 * Math.Sin(fractionalYear)
                - 0.014615 * Math.Cos(2 * fractionalYear)
                - 0.040849 * Math.Sin(2 * fractionalYear));

            double declination = 0.006918
                - 0.399912 * Math.Cos(fractionalYear)
                + 0.070257 * Math.Sin(fractionalYear)
                - 0.006758 * Math.Cos(2 * fractionalYear)
                + 0.000907 * Math.Sin(2 * fractionalYear)
                - 0.002697 * Math.Cos(3 * fractionalYear)
                + 0.00148 * Math.Sin(3 * fractionalYear);

            double timeOffset = equationOfTime
                + 4 * longitude
                - 60 * timeOfDay.Offset.TotalHours;
            double trueSolarTime = fractionalHour * 60 + timeOffset;
            double hourAngle = DegreesToRadians(trueSolarTime / 4 - 180);
            double latitudeRadians = DegreesToRadians(latitude);

            double cosineZenith = Math.Sin(latitudeRadians) * Math.Sin(declination)
                + Math.Cos(latitudeRadians) * Math.Cos(declination) * Math.Cos(hourAngle);

            return FromDegrees(RadiansToDegrees(Math.Acos(Math.Clamp(cosineZenith, -1, 1))));
        }

        public int CompareTo(SolarZenith other) => Degrees.CompareTo(other.Degrees);

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
        private static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;
    }
}
