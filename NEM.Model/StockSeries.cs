using NEM.Model.Units;

namespace NEM.Model.Series
{
    /// <summary>
    /// A stock series in megawatt-hours (MWh): state of charge only. A stock is a
    /// level at an instant, not something computed with — it never appears in the
    /// energy balance and is a diagnostic output trajectory.
    /// <para>
    /// It has <b>no combination operators and no resample method, by design</b>. It
    /// cannot be summed as a flow because it cannot be summed at all — summing state
    /// of charge across intervals yields a number with no physical meaning.
    /// </para>
    /// <para>
    /// Resolution-change rule, for the record: a flow resamples by <b>mean</b>, a stock
    /// by <b>sum</b>. Neither is built here for stock — state of charge is never
    /// resampled in the model.
    /// </para>
    /// </summary>
    public sealed class StockSeries : TimeSeries
    {
        public StockSeries(DateTimeOffset start, TimeSpan resolution, double[] megawattHours)
            : base(start, resolution, megawattHours)
        {
            for (int i = 0; i < megawattHours.Length; i++)
            {
                if (megawattHours[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(megawattHours),
                        megawattHours[i],
                        $"State of charge cannot be negative (index {i}); stocks are unsigned. " +
                        "The upper bound (≤ capacity) is enforced where the storage fleet is known.");
                }
            }
        }

        /// <summary>
        /// State of charge at <paramref name="index"/> (MWh) — the level at the
        /// <b>start</b> of interval <c>index</c> (interval-beginning, per
        /// <see cref="TimeSeries.InstantAt"/>).
        /// </summary>
        public Energy this[int index] => Energy.FromMegawattHours(RawValue(index));
    }
}
