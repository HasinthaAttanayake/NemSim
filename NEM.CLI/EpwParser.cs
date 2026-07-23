using CsvHelper;
using CsvHelper.Configuration;
using NEM.Model.Series;
using System.Globalization;

namespace NEM.CLI;

internal sealed record EpwHeader(
    string City,
    string Wmo,
    double Latitude,
    double Longitude,
    double TimeZone,
    bool LeapYearObserved,
    int NumberOfDataPeriods,
    int RecordsPerHour,
    int DataStartLineNumber);

internal sealed record EpwRow(
    int Year,
    int Month,
    int Day,
    int Hour,
    string DataSourceAndUncertaintyFlags,
    double GlobalHorizontalRadiation,
    double DirectNormalRadiation,
    double WindSpeed);

internal sealed record EpwFile(EpwHeader Header, IReadOnlyList<EpwRow> Rows);

internal sealed record EpwWeatherSeries(
    TraceSeries DirectNormalRadiation,
    TraceSeries WindSpeed);

internal sealed record EpwGap(
    int RowNumber,
    int Month,
    int Day,
    int Hour,
    string FieldName,
    string RawValue);

internal sealed class EpwGapException : Exception
{
    public EpwGapException(IReadOnlyList<EpwGap> gaps)
        : base(BuildMessage(gaps))
    {
        Gaps = gaps;
    }

    public IReadOnlyList<EpwGap> Gaps { get; }

    private static string BuildMessage(IReadOnlyList<EpwGap> gaps)
    {
        return "EPW contains missing or invalid values:" + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                gaps.Select(gap =>
                    $"Row {gap.RowNumber} ({gap.Month:D2}/{gap.Day:D2} hour {gap.Hour}): " +
                    $"{gap.FieldName} = {gap.RawValue}"));
    }
}

internal static class EpwParser
{
    internal const int SyntheticNonLeapYear = 2001;
    private const int LeapCapableValidationYear = 2000;
    private const double EpwWindMeasurementHeightMetres = 10;
    private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);
    private static readonly TimeSpan HourlyResolution = TimeSpan.FromHours(1);
    internal static readonly DateTimeOffset SyntheticNonLeapStart =
        new(SyntheticNonLeapYear, 1, 1, 0, 0, 0, NemOffset);
    private static readonly CsvConfiguration CsvConfiguration = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        IgnoreBlankLines = false,
        TrimOptions = TrimOptions.Trim,
    };

    public static EpwHeader ReadHeader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? city = null;
        string? wmo = null;
        double latitude = 0;
        double longitude = 0;
        double timeZone = 0;
        bool? leapYearObserved = null;
        int? numberOfDataPeriods = null;
        int? recordsPerHour = null;
        int lineNumber = 0;

        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = ParseCsvLine(line);
            string keyword = fields[0];

            if (keyword.Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                RequireFieldCount(fields, 10, keyword, lineNumber);
                city = fields[1];
                wmo = fields[5];
                latitude = ParseDouble(fields[6], "Latitude", lineNumber);
                longitude = ParseDouble(fields[7], "Longitude", lineNumber);
                timeZone = ParseDouble(fields[8], "TimeZone", lineNumber);
            }
            else if (keyword.StartsWith("HOLIDAYS/DAYLIGHT SAVING", StringComparison.OrdinalIgnoreCase))
            {
                RequireFieldCount(fields, 2, keyword, lineNumber);
                leapYearObserved = fields[1] switch
                {
                    "Yes" => true,
                    "No" => false,
                    _ => throw new FormatException(
                        $"Line {lineNumber}: LeapYear Observed must be Yes or No; got '{fields[1]}'."),
                };
            }
            else if (keyword.Equals("DATA PERIODS", StringComparison.OrdinalIgnoreCase))
            {
                RequireFieldCount(fields, 3, keyword, lineNumber);
                numberOfDataPeriods = ParseInt(fields[1], "Number of Data Periods", lineNumber);
                recordsPerHour = ParseInt(fields[2], "Records per hour", lineNumber);
                break;
            }
        }

        if (city is null || wmo is null)
        {
            throw new FormatException("EPW header is missing the LOCATION record.");
        }

        if (leapYearObserved is null)
        {
            throw new FormatException("EPW header is missing the HOLIDAYS/DAYLIGHT SAVING record.");
        }

        if (numberOfDataPeriods is null || recordsPerHour is null)
        {
            throw new FormatException("EPW header is missing the DATA PERIODS record.");
        }

        return new EpwHeader(
            city,
            wmo,
            latitude,
            longitude,
            timeZone,
            leapYearObserved.Value,
            numberOfDataPeriods.Value,
            recordsPerHour.Value,
            lineNumber + 1);
    }

    public static EpwFile ReadRows(string path)
    {
        EpwHeader header = ReadHeader(path);
        var rows = new List<EpwRow>();
        int lineNumber = header.DataStartLineNumber - 1;

        foreach (string line in File.ReadLines(path).Skip(lineNumber))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new FormatException($"Line {lineNumber}: blank data row.");
            }

            string[] fields = ParseCsvLine(line);
            RequireFieldCount(fields, 22, "data row", lineNumber);
            rows.Add(new EpwRow(
                ParseInt(fields[0], "Year", lineNumber),
                ParseInt(fields[1], "Month", lineNumber),
                ParseInt(fields[2], "Day", lineNumber),
                ParseInt(fields[3], "Hour", lineNumber),
                fields[5],
                ParseDouble(fields[13], "Global Horizontal Radiation", lineNumber),
                ParseDouble(fields[14], "Direct Normal Radiation", lineNumber),
                ParseDouble(fields[21], "Wind Speed", lineNumber)));
        }

        IReadOnlyList<EpwGap> gaps = FindGaps(rows);
        if (gaps.Count > 0)
        {
            throw new EpwGapException(gaps);
        }

        return new EpwFile(header, rows);
    }

    public static EpwFile ReadValidated(string path)
    {
        EpwFile epw = ReadRows(path);
        ValidateStructure(epw);
        return epw;
    }

    public static EpwWeatherSeries ReadTimeSeries(string path)
    {
        EpwFile epw = ReadValidated(path);
        double[] directNormalRadiation = epw.Rows
            .Select(row => row.DirectNormalRadiation)
            .ToArray();
        double[] windSpeed = epw.Rows
            .Select(row => row.WindSpeed)
            .ToArray();

        TraceSeries directNormalRadiationSeries = TraceSeries.DirectNormalRadiation(
            SyntheticNonLeapStart,
            HourlyResolution,
            directNormalRadiation);
        TraceSeries windSpeedSeries = TraceSeries.WindSpeed(
            SyntheticNonLeapStart,
            HourlyResolution,
            windSpeed,
            EpwWindMeasurementHeightMetres);

        if (directNormalRadiationSeries.Resolution != HourlyResolution
            || windSpeedSeries.Resolution != HourlyResolution)
        {
            throw new InvalidOperationException("EPW traces must retain their native hourly resolution.");
        }

        return new EpwWeatherSeries(directNormalRadiationSeries, windSpeedSeries);
    }

    public static EpwProvenanceReport ReadProvenance(string path)
    {
        return EpwProvenance.Create(ReadValidated(path));
    }

    private static void ValidateStructure(EpwFile epw)
    {
        if (epw.Header.RecordsPerHour != 1)
        {
            throw new FormatException(
                $"Records per hour must be 1; got {epw.Header.RecordsPerHour}. Sub-hourly EPW data is not downsampled.");
        }

        if (epw.Header.NumberOfDataPeriods != 1)
        {
            throw new FormatException(
                $"Number of Data Periods must be 1; got {epw.Header.NumberOfDataPeriods}.");
        }

        int expectedRowCount = epw.Header.LeapYearObserved ? 8784 : 8760;
        if (epw.Rows.Count != expectedRowCount)
        {
            throw new FormatException(
                $"Row count must be {expectedRowCount} when LeapYear Observed is " +
                $"{(epw.Header.LeapYearObserved ? "Yes" : "No")}; got {epw.Rows.Count}.");
        }

        int validationYear = epw.Header.LeapYearObserved
            ? LeapCapableValidationYear
            : SyntheticNonLeapYear;
        var expectedStart = new DateTime(validationYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        for (int index = 0; index < epw.Rows.Count; index++)
        {
            EpwRow row = epw.Rows[index];
            int rowNumber = index + 1;
            if (row.Hour is < 1 or > 24)
            {
                throw new FormatException(
                    $"Row {rowNumber}: Hour must be in 1..24; got {row.Hour}.");
            }

            DateTime rowDate;
            try
            {
                rowDate = new DateTime(
                    validationYear,
                    row.Month,
                    row.Day,
                    row.Hour - 1,
                    0,
                    0,
                    DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new FormatException(
                    $"Row {rowNumber}: invalid month/day {row.Month}/{row.Day}.", exception);
            }

            DateTime expectedDate = expectedStart.AddHours(index);
            if (rowDate != expectedDate)
            {
                throw new FormatException(
                    $"Row {rowNumber}: expected month/day/hour " +
                    $"{expectedDate.Month}/{expectedDate.Day} {expectedDate.Hour + 1}; " +
                    $"got {row.Month}/{row.Day} {row.Hour}. " +
                    "EPW rows must be contiguous hourly records; source year is intentionally ignored.");
            }
        }

        if (epw.Header.TimeZone != 10)
        {
            throw new FormatException(
                $"LOCATION TimeZone must be 10 for NEM time; got {epw.Header.TimeZone}.");
        }
    }

    private static IReadOnlyList<EpwGap> FindGaps(IReadOnlyList<EpwRow> rows)
    {
        var gaps = new List<EpwGap>();

        for (int index = 0; index < rows.Count; index++)
        {
            EpwRow row = rows[index];
            if (row.GlobalHorizontalRadiation >= 9999)
            {
                AddGap(gaps, index, row, "Global Horizontal Radiation", row.GlobalHorizontalRadiation);
            }

            if (row.DirectNormalRadiation >= 9999 || row.DirectNormalRadiation < 0)
            {
                AddGap(gaps, index, row, "Direct Normal Radiation", row.DirectNormalRadiation);
            }

            if (row.WindSpeed >= 999 || row.WindSpeed < 0 || row.WindSpeed > 40)
            {
                AddGap(gaps, index, row, "Wind Speed", row.WindSpeed);
            }
        }

        return gaps;
    }

    private static void AddGap(
        ICollection<EpwGap> gaps,
        int index,
        EpwRow row,
        string fieldName,
        double rawValue)
    {
        gaps.Add(new EpwGap(
            index + 1,
            row.Month,
            row.Day,
            row.Hour,
            fieldName,
            rawValue.ToString(CultureInfo.InvariantCulture)));
    }

    private static string[] ParseCsvLine(string line)
    {
        using var reader = new StringReader(line);
        using var parser = new CsvParser(reader, CsvConfiguration);
        return parser.Read() ? parser.Record ?? [] : [];
    }

    private static void RequireFieldCount(string[] fields, int minimum, string keyword, int lineNumber)
    {
        if (fields.Length < minimum)
        {
            throw new FormatException(
                $"Line {lineNumber}: {keyword} requires at least {minimum - 1} values after the keyword.");
        }
    }

    private static int ParseInt(string raw, string fieldName, int lineNumber)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"Line {lineNumber}: {fieldName} is not an integer: '{raw}'.");
        }

        return value;
    }

    private static double ParseDouble(string raw, string fieldName, int lineNumber)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new FormatException($"Line {lineNumber}: {fieldName} is not a number: '{raw}'.");
        }

        return value;
    }
}