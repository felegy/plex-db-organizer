using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;

namespace PlexTmdbSync.Core;

public class MovieSyncService
{
    private readonly AppDatabaseService _appDatabaseService;
    private readonly PlexDatabaseService _plexDatabaseService;
    private readonly TmdbService _tmdbService;
    private readonly CsvExportService _csvExportService;
    private readonly ILogger<MovieSyncService> _logger;

    public MovieSyncService(
        AppDatabaseService appDatabaseService,
        PlexDatabaseService plexDatabaseService,
        TmdbService tmdbService,
        CsvExportService csvExportService,
        ILogger<MovieSyncService> logger)
    {
        _appDatabaseService = appDatabaseService;
        _plexDatabaseService = plexDatabaseService;
        _tmdbService = tmdbService;
        _csvExportService = csvExportService;
        _logger = logger;
    }

    public async Task MigrateOnlyAsync()
    {
        await _appDatabaseService.InitializeAsync();
        _logger.LogInformation("Migration-only mode completed. Exiting without Plex sync or CSV export.");
    }

    public async Task<List<PlexMovie>> RunSyncAsync(bool tmdbEnrich, int batchSize, string outputPath)
    {
        await _appDatabaseService.InitializeAsync();

        _logger.LogInformation("Step 1: Fetching movies from Plex database...");
        var movies = await _plexDatabaseService.GetMoviesAsync();

        if (movies.Count == 0)
        {
            _logger.LogWarning("No movies found in Plex database");
            return movies;
        }

        if (tmdbEnrich)
        {
            _logger.LogInformation("Step 2: Enriching data with TMDB information...");
            var totalMovies = movies.Count;

            for (int i = 0; i < totalMovies; i += batchSize)
            {
                var batch = movies.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(m => _tmdbService.EnrichMovieWithTmdbDataAsync(m)).ToList();
                await Task.WhenAll(tasks);

                var progress = Math.Min(i + batchSize, totalMovies);
                _logger.LogInformation("Progress: {Progress}/{Total} movies enriched", progress, totalMovies);
            }
        }

        _logger.LogInformation("Step 3: Saving movies into app database...");
        await _appDatabaseService.UpsertMoviesAsync(movies);

        _logger.LogInformation("Step 4: Exporting to CSV from app database...");
        var moviesFromAppDb = await _appDatabaseService.GetMoviesAsync();
        await _csvExportService.ExportMoviesToCsvAsync(moviesFromAppDb, outputPath);

        _logger.LogInformation("Sync completed successfully!");
        return moviesFromAppDb;
    }
}
