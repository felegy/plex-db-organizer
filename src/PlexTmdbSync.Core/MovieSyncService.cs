using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;

namespace PlexTmdbSync.Core;

public class MovieSyncService
{
    private readonly ConfigService _config;
    private readonly AppDatabaseService _appDatabaseService;
    private readonly PlexDatabaseService _plexDatabaseService;
    private readonly TmdbService _tmdbService;
    private readonly ImdbService _imdbService;
    private readonly CsvExportService _csvExportService;
    private readonly ILogger<MovieSyncService> _logger;

    public MovieSyncService(
        ConfigService config,
        AppDatabaseService appDatabaseService,
        PlexDatabaseService plexDatabaseService,
        TmdbService tmdbService,
        ImdbService imdbService,
        CsvExportService csvExportService,
        ILogger<MovieSyncService> logger)
    {
        _config = config;
        _appDatabaseService = appDatabaseService;
        _plexDatabaseService = plexDatabaseService;
        _tmdbService = tmdbService;
        _imdbService = imdbService;
        _csvExportService = csvExportService;
        _logger = logger;
    }

    public async Task MigrateOnlyAsync()
    {
        await _appDatabaseService.InitializeAsync();
        _logger.LogInformation("Migration-only mode completed. Exiting without Plex sync or CSV export.");
    }

    public async Task<List<PlexMovie>> RunSyncAsync(bool tmdbEnrich, bool imdbEnrich, int batchSize, string outputPath)
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

        if (imdbEnrich)
        {
            _logger.LogInformation("Step 2b: Enriching data with IMDB information...");
            var totalMovies = movies.Count;
            var maxParallelism = _config.ImdbMaxParallelism;
            var existingMovies = await _appDatabaseService.GetMoviesAsync();
            if (existingMovies.Count > 0)
            {
                var existingByTmdbId = existingMovies
                    .Where(m => m.TmdbId > 0 && !string.IsNullOrWhiteSpace(m.ImdbId))
                    .ToDictionary(m => m.TmdbId);
                foreach (var movie in movies)
                {
                    if (movie.TmdbId > 0 && existingByTmdbId.TryGetValue(movie.TmdbId, out var existing))
                    {
                        movie.ImdbId = existing.ImdbId;
                        movie.ImdbRating = existing.ImdbRating;
                        movie.ImdbVotes = existing.ImdbVotes;
                        movie.ImdbUrl = existing.ImdbUrl;
                    }
                }
                var preloaded = movies.Count(m => !string.IsNullOrWhiteSpace(m.ImdbId));
                _logger.LogInformation("Pre-loaded IMDB data for {Count}/{Total} movies from app database", preloaded, movies.Count);
            }

            _logger.LogInformation("IMDB enrichment parallelism set to {Parallelism}", maxParallelism);

            for (int i = 0; i < totalMovies; i += batchSize)
            {
                var batch = movies.Skip(i).Take(batchSize).ToList();
                using var semaphore = new SemaphoreSlim(maxParallelism);
                var tasks = batch.Select(async movie =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await _imdbService.EnrichMovieWithImdbDataAsync(movie);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();
                await Task.WhenAll(tasks);

                var progress = Math.Min(i + batchSize, totalMovies);
                _logger.LogInformation("IMDB progress: {Progress}/{Total} movies enriched", progress, totalMovies);
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
