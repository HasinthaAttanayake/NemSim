namespace NEM.Model.Series
{
    /// <summary>
    /// NEM market time: fixed AEST (UTC+10), no daylight saving, all regions and all
    /// year. Never infer an offset from the machine locale — local time gives a
    /// duplicated hour each October and a missing one each April, producing a series
    /// one value too long or too short and a silent alignment failure.
    /// </summary>
    internal static class NemTime
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(10);

        /// <summary>
        /// Throws unless <paramref name="instant"/> is expressed in NEM market time
        /// (offset +10:00).
        /// </summary>
        public static void Require(DateTimeOffset instant, string paramName)
        {
            if (instant.Offset != Offset)
            {
                throw new ArgumentException(
                    $"Timestamps must be in NEM market time (UTC+10); got offset {instant.Offset}. " +
                    "Do not infer the offset from the machine locale.",
                    paramName);
            }
        }
    }

    /// <summary>
    /// Base for the model's time-series value objects. Carries the start instant,
    /// resolution and length, pins the index → instant mapping, and enforces the
    /// completeness and NEM-time guarantees at construction.
    /// <para>
    /// Category (flow / stock / trace) is the concrete type, not a runtime tag. Values
    /// are held as a private <c>double[]</c> copied defensively so the series is
    /// immutable and round-trips cleanly; the unit interpretation of those doubles is
    /// fixed by the derived type.
    /// </para>
    /// <para>
    /// <b>Timestamp convention: interval-beginning.</b> <c>InstantAt(index)</c> is
    /// <c>Start + index × Resolution</c>, so index <c>i</c> labels the instant the
    /// interval <i>begins</i>. Both external sources label interval-ending and must be
    /// converted at parse time before constructing a series — getting one source right
    /// and the other wrong produces a silent one-interval offset between series:
    /// <list type="bullet">
    /// <item><description>
    /// <b>EPW</b> labels by hour number 1–24 ("Hour 1 is 00:01 to 01:00"); each
    /// radiation field is received during the minutes preceding the label. Map
    /// <c>Start</c> = midnight of day one and hour <i>h</i> → index <i>h−1</i>.
    /// </description></item>
    /// <item><description>
    /// <b>AEMO</b> labels each timestamp at the interval end. Subtract one
    /// resolution from the label to get the interval-beginning instant.
    /// </description></item>
    /// </list>
    /// </para>
    /// </summary>
    public abstract class TimeSeries
    {
        private readonly double[] _values;

        private protected TimeSeries(DateTimeOffset start, TimeSpan resolution, double[] values)
        {
            NemTime.Require(start, nameof(start));

            if (resolution <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolution), resolution, "Resolution must be positive.");
            }

            ArgumentNullException.ThrowIfNull(values);

            if (values.Length == 0)
            {
                throw new ArgumentException(
                    "A series must have at least one value; gaps surface as parse errors, not empty series.",
                    nameof(values));
            }

            for (int i = 0; i < values.Length; i++)
            {
                double value = values[i];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentException(
                        $"A constructed series is complete: no NaN or infinity (index {i}).",
                        nameof(values));
                }
            }

            Start = start;
            Resolution = resolution;
            _values = (double[])values.Clone();
        }

        /// <summary>First instant of the series, in NEM market time (UTC+10).</summary>
        public DateTimeOffset Start { get; }

        /// <summary>Interval between successive values.</summary>
        public TimeSpan Resolution { get; }

        /// <summary>Number of values in the series.</summary>
        public int Length => _values.Length;

        /// <summary>
        /// Instant at <paramref name="index"/>: <c>Start + index × Resolution</c> —
        /// the instant the interval begins (interval-beginning convention).
        /// </summary>
        public DateTimeOffset InstantAt(int index)
        {
            RequireInRange(index);
            return Start + TimeSpan.FromTicks(Resolution.Ticks * index);
        }

        /// <summary>Raw value at <paramref name="index"/>; unit is defined by the derived type.</summary>
        protected double RawValue(int index)
        {
            RequireInRange(index);
            return _values[index];
        }

        /// <summary>
        /// Throws if <paramref name="other"/> is not aligned to this series on start,
        /// resolution and length, naming the mismatch. Never coerces or truncates.
        /// </summary>
        protected void RequireAligned(TimeSeries other)
        {
            ArgumentNullException.ThrowIfNull(other);

            if (Start != other.Start)
            {
                throw new ArgumentException(
                    $"Series are misaligned on start: {Start:o} vs {other.Start:o}.",
                    nameof(other));
            }

            if (Resolution != other.Resolution)
            {
                throw new ArgumentException(
                    $"Series are misaligned on resolution: {Resolution} vs {other.Resolution}.",
                    nameof(other));
            }

            if (Length != other.Length)
            {
                throw new ArgumentException(
                    $"Series are misaligned on length: {Length} vs {other.Length}.",
                    nameof(other));
            }
        }

        private void RequireInRange(int index)
        {
            if (index < 0 || index >= _values.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"Index must be in [0, {_values.Length - 1}].");
            }
        }
    }
}
