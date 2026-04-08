using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;

namespace PlexTmdbSync.Core;

public class MovieSyncService
{
    private readonly AppDatabaseService _appDatabaseService;
    private readonly PlexDatabaseService _plexDatabaseService;
    private readonly TmdbService _tmdbService;
    private readonly OmdbService _omdbService;
    private readonly CsvExportService _csvExportService;
    private readonly ILogger<MovieSyncService> _logger;

    public MovieSyncService(
        AppDatabaseService appDatabaseService,
        PlexDatabaseService plexDatabaseService,
        TmdbService tmdbService,
        OmdbService omdbService,
        CsvExportService csvExportService,
        ILogger<MovieSyncService> logger)
    {
        _appDatabaseService = appDatabaseService;
        _plexDatabaseService = plexDatabaseService;
        _tmdbService = tmdbService;
        _omdbService = omdbService;
        _csvExportService = csvExportService;
        _logger = logger;
    }

    public async Task MigrateOnlyAsync()
    {
        await _appDatabaseService.InitializeAsync();
        _logger.LogInformation("Migration-only mode completed. Exiting without Plex sync or CSV export.");
    }

    public async Task<List<PlexMovie>> RunSyncAsync(bool tmdbEnrich, bool omdbEnrich, int batchSize, string outputPath)
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

        if (omdbEnrich)
        {
            _logger.LogInformation("Step 2b: Enriching data with OMDb information...");

            var existingMovies = await _appDatabaseService.GetMoviesAsync();
            var existingById = existingMovies
                .Where(movie => movie.Id > 0 && !string.IsNullOrWhiteSpace(movie.ImdbId))
                .ToDictionary(movie => movie.Id);

            foreach (var movie in movies)
            {
                if (!existingById.TryGetValue(movie.Id, out var existing))
                    continue;

                movie.ImdbId = existing.ImdbId;
                movie.ImdbRating = existing.ImdbRating;
                movie.ImdbVotes = existing.ImdbVotes;
                movie.ImdbUrl = existing.ImdbUrl;
            }

            var moviesToEnrich = movies.Where(movie => string.IsNullOrWhiteSpace(movie.ImdbId)).ToList();
            _logger.LogInformation(
                "Pre-loaded OMDb data for {Preloaded}/{Total} movies from app database",
                movies.Count - moviesToEnrich.Count,
                movies.Count);

            var totalMovies = moviesToEnrich.Count;
            for (int i = 0; i < totalMovies; i += batchSize)
            {
                var batch = moviesToEnrich.Skip(i).Take(batchSize).ToList();
                foreach (var movie in batch)
                {
                    await _omdbService.EnrichMovieWithOmdbDataAsync(movie);
                }

                var progress = Math.Min(i + batchSize, totalMovies);
                _logger.LogInformation("OMDb progress: {Progress}/{Total} movies enriched", progress, totalMovies);
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
