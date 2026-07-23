using System.Text.Json;

namespace NEM.CLI;

internal sealed record EpwWindRun(int Length, DateTimeOffset? StartTimestamp);

internal sealed record EpwWindProvenance(
    IReadOnlyDictionary<string, double> ShareBySourceCode,
    EpwWindRun LongestConsecutiveNonARun);

internal sealed record EpwProvenanceReport(
    int RowCount,
    IReadOnlyDictionary<string, double> DaylightDniSourceShares,
    IReadOnlyDictionary<string, int> DaylightDniUncertaintyHistogram,
    EpwWindProvenance Wind,
    bool LeapYearObserved,
    IReadOnlyList<int> SourceYears,
    int FlagsUnavailable);

internal static class EpwProvenance
{
    private static readonly string[] SolarCategories = ["Measured", "Derived", "Modelled", "Unknown"];
    private static readonly string[] WindSourceCodes = ["A", "B", "C", "E", "F", "?", "Unknown"];

    public static EpwProvenanceReport Create(EpwFile epw)
    {
        var daylightDniSources = SolarCategories.ToDictionary(category => category, _ => 0);
        var daylightDniUncertainty = new Dictionary<string, int>();
        var windSources = WindSourceCodes.ToDictionary(code => code, _ => 0);
        int flagsUnavailable = 0;
        int daylightHours = 0;
        int longestWindRunLength = 0;
        int longestWindRunStartIndex = -1;
        int currentWindRunLength = 0;
        int currentWindRunStartIndex = -1;

        for (int index = 0; index < epw.Rows.Count; index++)
        {
            EpwRow row = epw.Rows[index];
            bool rowFlagsUnavailable = false;
            _ = DecodeSolar(row.DataSourceAndUncertaintyFlags, 10, ref rowFlagsUnavailable);
            DecodedFlag dni = DecodeSolar(row.DataSourceAndUncertaintyFlags, 12, ref rowFlagsUnavailable);
            DecodedFlag wind = DecodeWind(row.DataSourceAndUncertaintyFlags, 26, ref rowFlagsUnavailable);

            if (rowFlagsUnavailable)
            {
                flagsUnavailable++;
            }

            if (row.GlobalHorizontalRadiation > 0)
            {
                daylightHours++;
                daylightDniSources[dni.Source]++;
                daylightDniUncertainty[dni.UncertaintyBand] =
                    daylightDniUncertainty.GetValueOrDefault(dni.UncertaintyBand) + 1;
            }

            windSources[wind.Source]++;
            if (wind.Source == "A")
            {
                FinishWindRun(
                    ref longestWindRunLength,
                    ref longestWindRunStartIndex,
                    currentWindRunLength,
                    currentWindRunStartIndex);
                currentWindRunLength = 0;
                currentWindRunStartIndex = -1;
            }
            else
            {
                if (currentWindRunLength == 0)
                {
                    currentWindRunStartIndex = index;
                }

                currentWindRunLength++;
            }
        }

        FinishWindRun(
            ref longestWindRunLength,
            ref longestWindRunStartIndex,
            currentWindRunLength,
            currentWindRunStartIndex);

        IReadOnlyDictionary<string, double> daylightShares = daylightDniSources.ToDictionary(
            pair => pair.Key,
            pair => daylightHours == 0 ? 0 : pair.Value * 100.0 / daylightHours);
        IReadOnlyDictionary<string, double> windShares = windSources.ToDictionary(
            pair => pair.Key,
            pair => epw.Rows.Count == 0 ? 0 : pair.Value * 100.0 / epw.Rows.Count);
        DateTimeOffset? windRunStart = longestWindRunStartIndex < 0
            ? null
            : EpwParser.SyntheticNonLeapStart + TimeSpan.FromHours(longestWindRunStartIndex);

        return new EpwProvenanceReport(
            epw.Rows.Count,
            daylightShares,
            daylightDniUncertainty,
            new EpwWindProvenance(
                windShares,
                new EpwWindRun(longestWindRunLength, windRunStart)),
            epw.Header.LeapYearObserved,
            epw.Rows.Select(row => row.Year).Distinct().Order().ToArray(),
            flagsUnavailable);
    }

    public static void WriteJson(EpwProvenanceReport report, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, options));
    }

    private static DecodedFlag DecodeSolar(string flags, int offset, ref bool unavailable)
    {
        if (!TryReadPair(flags, offset, out char source, out char uncertainty))
        {
            unavailable = true;
            return new DecodedFlag("Unknown", "Unknown");
        }

        string category = source switch
        {
            'A' or 'B' or 'C' => "Measured",
            'D' => "Derived",
            'E' or 'F' or 'G' or 'H' => "Modelled",
            '?' => "Unknown",
            _ => "Unknown",
        };
        if (source is not ('A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G' or 'H' or '?'))
        {
            unavailable = true;
        }

        return new DecodedFlag(category, DecodeUncertainty(uncertainty, ref unavailable));
    }

    private static DecodedFlag DecodeWind(string flags, int offset, ref bool unavailable)
    {
        if (!TryReadPair(flags, offset, out char source, out char uncertainty))
        {
            unavailable = true;
            return new DecodedFlag("Unknown", "Unknown");
        }

        string sourceCode = source is 'A' or 'B' or 'C' or 'E' or 'F' or '?'
            ? source.ToString()
            : "Unknown";
        if (sourceCode == "Unknown")
        {
            unavailable = true;
        }

        return new DecodedFlag(sourceCode, DecodeUncertainty(uncertainty, ref unavailable));
    }

    private static bool TryReadPair(string flags, int offset, out char source, out char uncertainty)
    {
        if (flags.Length <= offset + 1)
        {
            source = default;
            uncertainty = default;
            return false;
        }

        source = flags[offset];
        uncertainty = flags[offset + 1];
        return true;
    }

    private static string DecodeUncertainty(char digit, ref bool unavailable)
    {
        string? band = digit switch
        {
            '0' => "n/a",
            '1' => "unused",
            '2' => "2-4%",
            '3' => "4-6%",
            '4' => "6-9%",
            '5' => "9-13%",
            '6' => "13-18%",
            '7' => "18-25%",
            '8' => "25-35%",
            '9' => "35-50%",
            _ => null,
        };
        if (band is null)
        {
            unavailable = true;
            return "Unknown";
        }

        return band;
    }

    private static void FinishWindRun(
        ref int longestLength,
        ref int longestStartIndex,
        int currentLength,
        int currentStartIndex)
    {
        if (currentLength > longestLength)
        {
            longestLength = currentLength;
            longestStartIndex = currentStartIndex;
        }
    }

    private sealed record DecodedFlag(string Source, string UncertaintyBand);
}