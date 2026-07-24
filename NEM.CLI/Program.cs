using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using CsvHelper.TypeConversion;
using System.Globalization;
using System.Text.Json;
using NEM.Contracts;

namespace NEM.CLI
{
    class Program
    {
        // AEMO NEM time is UTC+10 fixed, no daylight saving
        private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);

        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--epw-report")
            {
                EpwWeatherSeries weather = EpwParser.ReadTimeSeries(args[1]);
                EpwProvenanceReport report = EpwParser.ReadProvenance(args[1]);
                string outputDirectory = Path.GetDirectoryName(GetDefaultOutputPath())!;
                string provenanceOutputPath = Path.Combine(
                    outputDirectory,
                    "weather-provenance.json");
                string weatherDataOutputPath = Path.Combine(outputDirectory, "weather-data.json");
                WeatherDataDTO weatherData = EpwWeatherExport.Create(
                    EpwParser.ReadHeader(args[1]),
                    weather,
                    Path.GetFileName(args[1]));
                EpwProvenance.WriteJson(report, provenanceOutputPath);
                EpwWeatherExport.WriteJson(weatherData, weatherDataOutputPath);
                Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }));
                Console.WriteLine(
                    $"Daylight DNI shares total: {report.DaylightDniSourceShares.Values.Sum():F2}%");
                Console.WriteLine(
                    $"Constructed {weather.GlobalHorizontalRadiation.Length} GHI, " +
                    $"{weather.DirectNormalRadiation.Length} DNI, " +
                    $"{weather.DiffuseHorizontalRadiation.Length} DHI, " +
                    $"{weather.SolarZenith.Length} solar zenith, and " +
                    $"{weather.WindSpeed.Length} wind values.");
                Console.WriteLine($"Wrote provenance report to: {Path.GetFullPath(provenanceOutputPath)}");
                Console.WriteLine($"Wrote weather data to: {Path.GetFullPath(weatherDataOutputPath)}");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-series")
            {
                EpwWeatherSeries weather = EpwParser.ReadTimeSeries(args[1]);
                Console.WriteLine(
                    $"GHI: {weather.GlobalHorizontalRadiation.Length}; " +
                    $"DNI: {weather.DirectNormalRadiation.Length}; " +
                    $"DHI: {weather.DiffuseHorizontalRadiation.Length}; " +
                    $"Solar zenith: {weather.SolarZenith.Length}; " +
                    $"Wind: {weather.WindSpeed.Length} hourly values; " +
                    $"First timestamp: {weather.DirectNormalRadiation.InstantAt(0):o}");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-validate")
            {
                EpwFile epw = EpwParser.ReadValidated(args[1]);
                Console.WriteLine(
                    $"All structural validations passed for {epw.Rows.Count} rows; " +
                    $"source years: {string.Join(", ", epw.Rows.Select(row => row.Year).Distinct().Order())}");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-gaps")
            {
                try
                {
                    EpwFile epw = EpwParser.ReadRows(args[1]);
                    Console.WriteLine($"Rows: {epw.Rows.Count}; Gaps: 0");
                    return 0;
                }
                catch (EpwGapException exception)
                {
                    Console.Error.WriteLine(exception.Message);
                    return 1;
                }
            }

            if (args.Length == 2 && args[0] == "--epw-rows")
            {
                EpwFile epw = EpwParser.ReadRows(args[1]);
                Console.WriteLine($"Rows: {epw.Rows.Count}");
                PrintEpwRow(epw.Rows, 1);
                PrintEpwRow(epw.Rows, 4380);
                PrintEpwRow(epw.Rows, epw.Rows.Count);
                return 0;
            }

            if (args.Length == 2 && args[0] == "--epw-header")
            {
                EpwHeader header = EpwParser.ReadHeader(args[1]);
                Console.WriteLine(
                    $"City: {header.City}; TimeZone: {header.TimeZone}; " +
                    $"RecordsPerHour: {header.RecordsPerHour}; LeapYearObserved: {header.LeapYearObserved}; " +
                    $"DataStartLine: {header.DataStartLineNumber}");
                return 0;
            }

            Console.WriteLine("Reading files in data folder");

            string dataPath = @"./data";
            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
                Console.WriteLine($"Created data directory at {Path.GetFullPath(dataPath)}");
            }

            string[] files = Directory.GetFiles(dataPath);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch = args => args.Header.ToLower(),
            };

            var rawDemandRecord = files.SelectMany(filePath =>
            {
                Console.WriteLine(filePath);
                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, config);
                // materialize here so we don't return an enumerable that uses a disposed reader
                return csv.GetRecords<AemoPriceAndDemand>().ToList();
            }).ToList();

            Console.WriteLine($"Lines of Demand Data Read: {rawDemandRecord.Count}");

            Scenario scenario = new Scenario("nsw demand only june 2026","nsw",rawDemandRecord.Min(x => x.SettlementDate), rawDemandRecord.Max(x => x.SettlementDate), rawDemandRecord.ElementAt(1).SettlementDate - rawDemandRecord.ElementAt(0).SettlementDate,"no agg");
            Sources dataSources = new Sources(files.Select(Path.GetFileName).ToArray()!);
            Series dataSeries = new Series(rawDemandRecord.Select(x => x.TotalDemand).ToArray());

            ModelInputOutputDTO modelOutputDTO = new ModelInputOutputDTO(1, scenario, DateTimeOffset.UtcNow,dataSources,dataSeries);

            // Write results to JSON
            string outputPath = args.Length > 0 
                ? args[0] 
                : GetDefaultOutputPath();

            try
            {
                string directory = Path.GetDirectoryName(outputPath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(modelOutputDTO, options);
                File.WriteAllText(outputPath, json);

                string absolutePath = Path.GetFullPath(outputPath);
                Console.WriteLine($"\nSuccessfully wrote results to: {absolutePath}");
                Console.WriteLine($"Data points: {dataSeries.DemandMw.Length}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\nError writing JSON file: {ex.Message}");
                return 1;
            }
        }

        private static void PrintEpwRow(IReadOnlyList<EpwRow> rows, int rowNumber)
        {
            EpwRow row = rows[rowNumber - 1];
            Console.WriteLine(
                $"Row {rowNumber}: DNI={row.DirectNormalRadiation}; WindSpeed={row.WindSpeed}");
        }

        static string GetDefaultOutputPath()
        {
            // Find solution root by looking for NEM.Web project directory
            string currentDir = AppContext.BaseDirectory;
            string solutionRoot = currentDir;

            // Navigate up from bin/Debug/net10.0 to project root, then to solution root
            for (int i = 0; i < 10; i++)
            {
                if (Directory.Exists(Path.Combine(solutionRoot, "NEM.Web")))
                {
                    break;
                }
                string? parent = Directory.GetParent(solutionRoot)?.FullName;
                if (parent == null) break;
                solutionRoot = parent;
            }

            return Path.Combine(solutionRoot, "NEM.Web", "wwwroot", "data", "results.json");
        }
    }

    // Custom type converter for NEM timestamps (UTC+10 fixed)
    public class NemDateTimeOffsetConverter : DefaultTypeConverter
    {
        private static readonly TimeSpan NemOffset = TimeSpan.FromHours(10);

        public override object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Parse as DateTime first with explicit format
            var dt = DateTime.ParseExact(text, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None);

            // Construct DateTimeOffset with explicit NEM offset
            return new DateTimeOffset(dt, NemOffset);
        }
    }

    // AEMO Data Class 
    public class AemoPriceAndDemand
    {
        public string Region { get; set; } = string.Empty;

        [TypeConverter(typeof(NemDateTimeOffsetConverter))]
        public DateTimeOffset SettlementDate { get; set; }

        // TODO: TotalDemand is power in MW (interval average), not energy in MWh despite the name. Wrap in Power and convert via Energy.From.
        public Double TotalDemand { get; set; }
        public Double Rrp {  get; set; }
        public string PeriodType { get; set; } = string.Empty;
    }
}

