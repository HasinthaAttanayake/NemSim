using FluentAssertions;
using NEM.Model.Series;

namespace NEM.CLI.Tests;

public sealed class EpwParserTests
{
    private const int DefaultSourceYear = 2025;
    private const int LeapCalendarYear = 2024;
    private const int NonLeapCalendarYear = 2025;

    [Fact]
    public void ReadHeader_ReturnsRequestedRecords_ForGoldenFixture()
    {
        var fixture = new EpwFixture
        {
            City = "Golden City",
            Wmo = "001234",
            Latitude = -33.5,
            Longitude = 151.25,
            TimeZone = 10,
            LeapYearObserved = false,
        };
        AddGoldenRows(fixture, 48);
        string path = fixture.Write();

        try
        {
            EpwHeader header = EpwParser.ReadHeader(path);

            header.City.Should().Be("Golden City");
            header.Wmo.Should().Be("001234");
            header.Latitude.Should().Be(-33.5);
            header.Longitude.Should().Be(151.25);
            header.TimeZone.Should().Be(10);
            header.LeapYearObserved.Should().BeFalse();
            header.NumberOfDataPeriods.Should().Be(1);
            header.RecordsPerHour.Should().Be(1);
            header.DataStartLineNumber.Should().Be(9);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRows_ReturnsSelectedColumns_ForGoldenFixture()
    {
        var fixture = new EpwFixture();
        AddGoldenRows(fixture, 48);
        string path = fixture.Write();

        try
        {
            EpwFile epw = EpwParser.ReadRows(path);

            epw.Rows.Should().HaveCount(48);
            epw.Rows[0].Should().Be(new EpwRow(
                DefaultSourceYear, 1, 1, 1, "FLAGS-0", 100, 200, 3));
            epw.Rows[23].Should().Be(new EpwRow(
                DefaultSourceYear, 1, 1, 24, "FLAGS-23", 123, 246, 5.3));
            epw.Rows[47].Should().Be(new EpwRow(
                DefaultSourceYear, 1, 2, 24, "FLAGS-47", 147, 294, 7.7));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadHeader_FindsDataPeriods_WhenHeaderIsRagged()
    {
        string path = new EpwFixture
        {
            BlankLineBeforeDataPeriods = true,
            DataPeriodsTrailingText = "   ",
        }.AddRow(DefaultSourceYear, 1, 1, 1).Write();

        try
        {
            EpwHeader header = EpwParser.ReadHeader(path);

            header.DataStartLineNumber.Should().Be(10);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(99999)]
    public void ReadRows_ThrowsGapManifest_WhenDniAtOrAboveSentinel(double dni)
    {
        string path = new EpwFixture()
            .AddRow(DefaultSourceYear, 1, 1, 1, directNormalRadiation: dni)
            .Write();

        try
        {
            var act = () => EpwParser.ReadRows(path);

            var exception = act.Should().Throw<EpwGapException>().Which;
            exception.Gaps.Should().ContainSingle();
            exception.Gaps[0].FieldName.Should().Be("Direct Normal Radiation");
            exception.Gaps[0].RawValue.Should().Be(dni.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRows_ThrowsGapManifest_WhenDniIsNegative()
    {
        string path = new EpwFixture()
            .AddRow(DefaultSourceYear, 1, 1, 1, directNormalRadiation: -1)
            .Write();

        try
        {
            var act = () => EpwParser.ReadRows(path);

            act.Should().Throw<EpwGapException>()
                .Which.Gaps.Should().ContainSingle(gap => gap.RawValue == "-1");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRows_DoesNotReportGap_WhenNighttimeDniIsZero()
    {
        string path = new EpwFixture().AddRow(
            DefaultSourceYear, 1, 1, 1, globalHorizontalRadiation: 0, directNormalRadiation: 0).Write();

        try
        {
            EpwFile epw = EpwParser.ReadRows(path);

            epw.Rows.Should().ContainSingle();
            epw.Rows[0].DirectNormalRadiation.Should().Be(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRows_ThrowsSingleCompleteManifest_WhenThreeGapsExist()
    {
        string path = new EpwFixture()
            .AddRow(DefaultSourceYear, 1, 1, 1, globalHorizontalRadiation: 9999)
            .AddRow(DefaultSourceYear, 1, 1, 2, directNormalRadiation: -1)
            .AddRow(DefaultSourceYear, 1, 1, 3, windSpeed: 41)
            .Write();

        try
        {
            var act = () => EpwParser.ReadRows(path);

            var exception = act.Should().Throw<EpwGapException>().Which;
            exception.Gaps.Should().HaveCount(3);
            exception.Gaps.Select(gap => gap.RowNumber).Should().Equal(1, 2, 3);
            exception.Message.Should().Contain("Global Horizontal Radiation");
            exception.Message.Should().Contain("Direct Normal Radiation");
            exception.Message.Should().Contain("Wind Speed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadValidated_AcceptsHour24FollowedByNextDayHour1()
    {
        string path = WriteFullYearFixture();

        try
        {
            EpwFile epw = EpwParser.ReadValidated(path);

            epw.Rows[23].Hour.Should().Be(24);
            epw.Rows[24].Hour.Should().Be(1);
            epw.Rows[24].Day.Should().Be(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadValidated_IgnoresSourceYear_WhenYearJumpsAtMonthBoundary()
    {
        string path = WriteFullYearFixture(
            sourceYear: date => date.Month == 1 ? 1998 : 2004);

        try
        {
            EpwFile epw = EpwParser.ReadValidated(path);

            epw.Rows[743].Year.Should().Be(1998);
            epw.Rows[744].Year.Should().Be(2004);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadValidated_ThrowsAtOffendingRow_WhenAnHourlyTimestampIsSkipped()
    {
        const int displacedIndex = 100;
        string path = WriteFullYearFixture(
            timestamp: (index, date, hour) => index == displacedIndex
                ? (date, hour + 1)
                : (date, hour));

        try
        {
            var act = () => EpwParser.ReadValidated(path);

            act.Should().Throw<FormatException>()
                .WithMessage($"*Row {displacedIndex + 1}: expected month/day/hour*got*contiguous hourly records*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadValidated_RejectsFebruary29_WhenLeapYearIsNotObserved()
    {
        int february28StartIndex = (31 + 27) * 24;
        string path = WriteFullYearFixture(
            timestamp: (index, date, hour) => index is >= (31 + 27) * 24 and < (31 + 28) * 24
                ? (new DateOnly(LeapCalendarYear, 2, 29), hour)
                : (date, hour));

        try
        {
            var act = () => EpwParser.ReadValidated(path);

            act.Should().Throw<FormatException>()
                .WithMessage($"*Row {february28StartIndex + 1}: invalid month/day 2/29*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadValidated_Accepts8784Rows_WhenLeapYearObserved()
    {
        string leapPath = WriteFullYearFixture(leapYearObserved: true, includeLeapDay: true);

        try
        {
            EpwFile epw = EpwParser.ReadValidated(leapPath);

            epw.Rows.Should().HaveCount(8784);
        }
        finally
        {
            File.Delete(leapPath);
        }
    }

    [Fact]
    public void ReadValidated_Throws_WhenLeapYearObservedHas8760Rows()
    {
        string path = WriteFullYearFixture(leapYearObserved: true, includeLeapDay: false);

        try
        {
            var act = () => EpwParser.ReadValidated(path);

            act.Should().Throw<FormatException>().WithMessage("*8784*8760*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadValidated_ThrowsClearMessage_WhenRecordsPerHourIsFour()
    {
        string path = WriteFullYearFixture(recordsPerHour: 4);

        try
        {
            var act = () => EpwParser.ReadValidated(path);

            act.Should().Throw<FormatException>().WithMessage("*Records per hour must be 1*got 4*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadValidated_ThrowsRatherThanShifting_WhenTimeZoneIsEight()
    {
        string path = WriteFullYearFixture(timeZone: 8);

        try
        {
            var act = () => EpwParser.ReadValidated(path);

            act.Should().Throw<FormatException>().WithMessage("*TimeZone must be 10*got 8*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadTimeSeries_ConstructsNativeHourlyTraces_InFixedNemTime()
    {
        string path = WriteFullYearFixture();

        try
        {
            EpwWeatherSeries weather = EpwParser.ReadTimeSeries(path);

            weather.DirectNormalRadiation.Length.Should().Be(8760);
            weather.DirectNormalRadiation.Unit.Should().Be(
                TraceUnit.DirectNormalRadiationWattHoursPerSquareMetre);
            weather.DirectNormalRadiation[0].Should().Be(0);
            weather.WindSpeed.Length.Should().Be(8760);
            weather.WindSpeed.Unit.Should().Be(TraceUnit.MetresPerSecond);
            weather.WindSpeed[0].Should().Be(5);
            weather.WindSpeed.MeasurementHeightMetres.Should().Be(10);
            weather.DirectNormalRadiation.Start.Should().Be(EpwParser.SyntheticNonLeapStart);
            weather.DirectNormalRadiation.Resolution.Should().Be(TimeSpan.FromHours(1));
            weather.WindSpeed.Start.Should().Be(weather.DirectNormalRadiation.Start);
            weather.WindSpeed.Resolution.Should().Be(TimeSpan.FromHours(1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadProvenance_RecordsUnknownWithoutThrowing_WhenFlagsAreTruncated()
    {
        string path = WriteFullYearFixture(
            flags: index => index == 0 ? "A0A0A0A0A0" : CompleteMeasuredFlags());

        try
        {
            EpwProvenanceReport report = EpwParser.ReadProvenance(path);

            report.FlagsUnavailable.Should().Be(1);
            report.Wind.ShareBySourceCode["Unknown"].Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadProvenance_ReportsRunAndStart_WhenSixWindFlagsAreC()
    {
        const int runStartIndex = 100;
        string path = WriteFullYearFixture(
            flags: index => WindFlags(index is >= runStartIndex and < runStartIndex + 6 ? 'C' : 'A'));

        try
        {
            EpwProvenanceReport report = EpwParser.ReadProvenance(path);

            report.Wind.LongestConsecutiveNonARun.Length.Should().Be(6);
            report.Wind.LongestConsecutiveNonARun.StartTimestamp.Should().Be(
                EpwParser.SyntheticNonLeapStart + TimeSpan.FromHours(runStartIndex));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteFullYearFixture(
        bool leapYearObserved = false,
        bool includeLeapDay = false,
        int recordsPerHour = 1,
        double timeZone = 10,
        Func<DateOnly, int>? sourceYear = null,
        Func<int, string>? flags = null,
        Func<int, DateOnly, int, (DateOnly Date, int Hour)>? timestamp = null)
    {
        var fixture = new EpwFixture
        {
            LeapYearObserved = leapYearObserved,
            RecordsPerHour = recordsPerHour,
            TimeZone = timeZone,
        };
        var start = new DateOnly(
            includeLeapDay ? LeapCalendarYear : NonLeapCalendarYear,
            1,
            1);
        int days = includeLeapDay ? 366 : 365;

        int rowIndex = 0;
        for (int dayIndex = 0; dayIndex < days; dayIndex++)
        {
            DateOnly date = start.AddDays(dayIndex);
            for (int hour = 1; hour <= 24; hour++)
            {
                (DateOnly rowDate, int rowHour) = timestamp?.Invoke(rowIndex, date, hour)
                    ?? (date, hour);
                fixture.AddRow(
                    sourceYear?.Invoke(date) ?? DefaultSourceYear,
                    rowDate.Month,
                    rowDate.Day,
                    rowHour,
                    flags: flags?.Invoke(rowIndex) ?? CompleteMeasuredFlags());
                rowIndex++;
            }
        }

        return fixture.Write();
    }

    private static string CompleteMeasuredFlags() => string.Concat(Enumerable.Repeat("A0", 22));

    private static string WindFlags(char windSource)
    {
        char[] flags = CompleteMeasuredFlags().ToCharArray();
        flags[26] = windSource;
        return new string(flags);
    }

    private static void AddGoldenRows(EpwFixture fixture, int count)
    {
        for (int index = 0; index < count; index++)
        {
            fixture.AddRow(
                DefaultSourceYear,
                1,
                1 + index / 24,
                1 + index % 24,
                100 + index,
                200 + 2 * index,
                3 + index / 10.0,
                $"FLAGS-{index}");
        }
    }
}