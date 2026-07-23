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

