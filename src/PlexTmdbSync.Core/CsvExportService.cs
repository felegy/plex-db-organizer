using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;
using System.Globalization;

namespace PlexTmdbSync.Core;

public class CsvExportService
{
    private readonly ILogger<CsvExportService> _logger;

    public CsvExportService(ILogger<CsvExportService> logger)
    {
        _logger = logger;
    }

    public async Task ExportMoviesToCsvAsync(List<PlexMovie> movies, string outputPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created directory: {Directory}", directory);
            }

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ",",
                Encoding = System.Text.Encoding.UTF8
            };

            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            using var csv = new CsvWriter(writer, config);

            csv.WriteHeader<PlexMovie>();
            await csv.NextRecordAsync();

            foreach (var movie in movies)
            {
                csv.WriteRecord(movie);
                await csv.NextRecordAsync();
            }

            _logger.LogInformation("Exported {Count} movies to {OutputPath}", movies.Count, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting movies to CSV");
            throw;
        }
    }
}
