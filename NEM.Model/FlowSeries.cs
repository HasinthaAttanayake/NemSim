using NEM.Model.Units;

namespace NEM.Model.Series
{
    /// <summary>
    /// A flow series in megawatts (MW): demand, per-fleet generation, charge/discharge
    /// rate, interconnector flow, residual. The workhorse of dispatch — everything in
    /// the energy balance is a flow.
    /// <para>
    /// A flow is meaningful to integrate over time (see <see cref="Integrate"/>) and to
    /// resample by mean (see <see cref="ResampleToHourly"/>). Values are stored so an
    /// indexed read returns a <see cref="Power"/> without allocating — the dispatch
    /// hour loop indexes into the series with scalars.
    /// </para>
    /// </summary>
    public sealed class FlowSeries : TimeSeries
    {
        public FlowSeries(DateTimeOffset start, TimeSpan resolution, double[] megawatts)
            : base(start, resolution, megawatts)
        {
        }

        /// <summary>Power at <paramref name="index"/> (MW).</summary>
        public Power this[int index] => Power.FromMegawatts(RawValue(index));

        /// <summary>Element-wise sum of two aligned flows.</summary>
        public FlowSeries Add(FlowSeries other)
        {
            RequireAligned(other);
            return Combine(other, static (a, b) => a + b);
        }

        /// <summary>Element-wise difference of two aligned flows (e.g. residual demand).</summary>
        public FlowSeries Subtract(FlowSeries other)
        {
            RequireAligned(other);
            return Combine(other, static (a, b) => a - b);
        }

        /// <summary>The non-negative part of each value (residual deficit).</summary>
        public FlowSeries PositivePart() => Map(static v => v > 0 ? v : 0);

        /// <summary>
        /// The non-positive part of each value (residual surplus), <b>sign-preserving</b>:
        /// a surplus reads as a negative MW here, not a magnitude. Charge code wanting the
        /// magnitude must negate (<c>-surplus[i]</c>).
        /// </summary>
        public FlowSeries NegativePart() => Map(static v => v < 0 ? v : 0);

        /// <summary>
        /// Caps each value at an <b>upper</b> bound of <paramref name="capacity"/> (fleet
        /// availability limit). One-sided: values below the cap, including negatives, pass
        /// through unchanged. Capacity must be non-negative.
        /// </summary>
        public FlowSeries ClampToCapacity(Power capacity)
        {
            double cap = capacity.Megawatts;
            if (cap < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), cap, "Capacity cannot be negative.");
            }

            return Map(v => v < cap ? v : cap);
        }

        /// <summary>Scales every value by <paramref name="factor"/> (build scale, data-centre scaling).</summary>
        public FlowSeries Scale(double factor) => Map(v => v * factor);

        /// <summary>
        /// Total <b>net</b> energy over the series: each interval contributes MW ×
        /// resolution hours, so the total is (sum of MW) × resolution hours. On a
        /// signed series positive and negative intervals cancel; this is net energy,
        /// not gross.
        /// </summary>
        public Energy Integrate()
        {
            double sumMegawatts = 0;
            for (int i = 0; i < Length; i++)
            {
                sumMegawatts += RawValue(i);
            }

            return Energy.FromMegawattHours(sumMegawatts * Resolution.TotalHours);
        }

        /// <summary>
        /// Resamples to hourly resolution by taking the <b>mean</b> of each hour's
        /// values. Averaging preserves the MW flow; summing would double it. Requires
        /// the resolution to divide one hour evenly and the length to be a whole number
        /// of hours.
        /// </summary>
        public FlowSeries ResampleToHourly()
        {
            if (Resolution == TimeSpan.FromHours(1))
            {
                return this;
            }

            long hourTicks = TimeSpan.FromHours(1).Ticks;
            if (hourTicks % Resolution.Ticks != 0)
            {
                throw new InvalidOperationException(
                    $"Resolution {Resolution} does not divide one hour evenly; cannot resample to hourly.");
            }

            int groupSize = (int)(hourTicks / Resolution.Ticks);
            if (Length % groupSize != 0)
            {
                throw new InvalidOperationException(
                    $"Length {Length} is not a whole number of hours at resolution {Resolution}; cannot resample to hourly.");
            }

            int hours = Length / groupSize;
            var hourly = new double[hours];
            for (int h = 0; h < hours; h++)
            {
                double sum = 0;
                int offset = h * groupSize;
                for (int j = 0; j < groupSize; j++)
                {
                    sum += RawValue(offset + j);
                }

                hourly[h] = sum / groupSize;
            }

            return new FlowSeries(Start, TimeSpan.FromHours(1), hourly);
        }

        private FlowSeries Combine(FlowSeries other, Func<double, double, double> op)
        {
            var result = new double[Length];
            for (int i = 0; i < Length; i++)
            {
                result[i] = op(RawValue(i), other.RawValue(i));
            }

            return new FlowSeries(Start, Resolution, result);
        }

        private FlowSeries Map(Func<double, double> op)
        {
            var result = new double[Length];
            for (int i = 0; i < Length; i++)
            {
                result[i] = op(RawValue(i));
            }

            return new FlowSeries(Start, Resolution, result);
        }
    }
}
