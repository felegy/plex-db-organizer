using Dapper;
using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;
using System.Data.SQLite;

namespace PlexTmdbSync.Core;

public class PlexDatabaseService
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly ILogger<PlexDatabaseService> _logger;

    public PlexDatabaseService(ConfigService config, ILogger<PlexDatabaseService> logger)
    {
        _logger = logger;
        _databasePath = Path.GetFullPath(config.PlexDatabasePath.Trim('\''));

        if (!File.Exists(_databasePath))
            throw new FileNotFoundException($"Plex database file was not found at '{_databasePath}'.", _databasePath);

        _connectionString = $"Data Source={_databasePath};Version=3;";
        _logger.LogInformation("Using Plex database at {DatabasePath}", _databasePath);
    }

    public async Task<List<PlexMovie>> GetMoviesAsync()
    {
        try
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogInformation("Connected to Plex database successfully");

            var query = @"
                SELECT 
                    mi.id as Id,
                    mi.title as Title,
                    mi.original_title as OriginalTitle,
                    MIN(mp.file) as FilePath,
                    CASE 
                        WHEN mi.guid LIKE '%themoviedb%' THEN CAST(SUBSTR(mi.guid, INSTR(mi.guid, '-') + 1, INSTR(SUBSTR(mi.guid, INSTR(mi.guid, '-') + 1), '?') - 1) AS INTEGER)
                        ELSE 0
                    END as TmdbId,
                    COALESCE(mi.year, 0) as Year,
                    mi.summary as Summary,
                    mi.rating as Rating,
                    COALESCE(mpi.duration, 0) / 60 as Duration,
                    COALESCE(mi.tags_genre, '') as Genres
                FROM metadata_items mi
                LEFT JOIN media_items mpi ON mi.id = mpi.metadata_item_id
                LEFT JOIN media_parts mp ON mpi.id = mp.media_item_id
                WHERE mi.metadata_type = 1
                GROUP BY mi.id
                ORDER BY mi.title";

            var movies = (await connection.QueryAsync<PlexMovie>(query)).ToList();
            _logger.LogInformation("Retrieved {Count} movies from Plex database", movies.Count);
            return movies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from Plex database");
            throw;
        }
    }
}
