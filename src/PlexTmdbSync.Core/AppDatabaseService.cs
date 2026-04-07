using Dapper;
using Microsoft.Extensions.Logging;
using PlexTmdbSync.Types;
using System.Data.SQLite;

namespace PlexTmdbSync.Core;

public class AppDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<AppDatabaseService> _logger;

    private sealed record Migration(int Version, string Description, string Sql);

    private static readonly IReadOnlyList<Migration> Migrations = new List<Migration>
    {
        new(
            1,
            "Create movies table",
            @"
            CREATE TABLE IF NOT EXISTS movies (
                Id INTEGER PRIMARY KEY,
                Title TEXT NOT NULL,
                OriginalTitle TEXT NULL,
                FilePath TEXT NULL,
                Year INTEGER NOT NULL,
                Summary TEXT NULL,
                Rating REAL NOT NULL,
                Duration INTEGER NOT NULL,
                Genres TEXT NULL,
                TmdbId INTEGER NOT NULL,
                TmdbPosterUrl TEXT NULL,
                TmdbRating REAL NULL,
                TmdbOverview TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );"
        ),
        new(
            2,
            "Add indexes for faster lookups",
            @"
            CREATE INDEX IF NOT EXISTS idx_movies_title ON movies(Title);
            CREATE INDEX IF NOT EXISTS idx_movies_original_title ON movies(OriginalTitle);
            CREATE INDEX IF NOT EXISTS idx_movies_tmdb_id ON movies(TmdbId);"
        ),
        new(
            3,
            "Add MustDelete column to movies",
            @"
            ALTER TABLE movies ADD COLUMN MustDelete INTEGER NOT NULL DEFAULT 0;"
        )
    };

    public AppDatabaseService(ConfigService config, ILogger<AppDatabaseService> logger)
    {
        _connectionString = config.AppDatabaseConnectionString;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        using var tx = connection.BeginTransaction();

        const string createMigrationTableSql = @"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                Version INTEGER PRIMARY KEY,
                Description TEXT NOT NULL,
                AppliedAtUtc TEXT NOT NULL
            );";

        await connection.ExecuteAsync(createMigrationTableSql, transaction: tx);

        var appliedVersions = (await connection.QueryAsync<int>(
            "SELECT Version FROM schema_migrations;",
            transaction: tx)).ToHashSet();

        var pending = Migrations
            .Where(m => !appliedVersions.Contains(m.Version))
            .OrderBy(m => m.Version)
            .ToList();

        if (pending.Count == 0)
        {
            tx.Commit();
            _logger.LogInformation("App database schema is up to date");
            return;
        }

        foreach (var migration in pending)
        {
            await connection.ExecuteAsync(migration.Sql, transaction: tx);
            await connection.ExecuteAsync(
                "INSERT INTO schema_migrations (Version, Description, AppliedAtUtc) VALUES (@Version, @Description, @AppliedAtUtc);",
                new
                {
                    migration.Version,
                    migration.Description,
                    AppliedAtUtc = DateTime.UtcNow.ToString("O")
                },
                tx);

            _logger.LogInformation("Applied migration {Version}: {Description}", migration.Version, migration.Description);
        }

        tx.Commit();
        _logger.LogInformation("Initialized app database schema with {Count} migration(s)", pending.Count);
    }

    public async Task UpsertMoviesAsync(IEnumerable<PlexMovie> movies)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        using var tx = connection.BeginTransaction();

        const string upsert = @"
            INSERT INTO movies (
                Id, Title, OriginalTitle, FilePath, Year, Summary, Rating, Duration, Genres,
                TmdbId, TmdbPosterUrl, TmdbRating, TmdbOverview, UpdatedAtUtc
            ) VALUES (
                @Id, @Title, @OriginalTitle, @FilePath, @Year, @Summary, @Rating, @Duration, @Genres,
                @TmdbId, @TmdbPosterUrl, @TmdbRating, @TmdbOverview, @UpdatedAtUtc
            )
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                OriginalTitle = excluded.OriginalTitle,
                FilePath = excluded.FilePath,
                Year = excluded.Year,
                Summary = excluded.Summary,
                Rating = excluded.Rating,
                Duration = excluded.Duration,
                Genres = excluded.Genres,
                TmdbId = excluded.TmdbId,
                TmdbPosterUrl = excluded.TmdbPosterUrl,
                TmdbRating = excluded.TmdbRating,
                TmdbOverview = excluded.TmdbOverview,
                UpdatedAtUtc = excluded.UpdatedAtUtc;";

        var rows = movies.Select(m => new
        {
            m.Id,
            m.Title,
            m.OriginalTitle,
            m.FilePath,
            m.Year,
            m.Summary,
            m.Rating,
            m.Duration,
            m.Genres,
            m.TmdbId,
            m.TmdbPosterUrl,
            m.TmdbRating,
            m.TmdbOverview,
            UpdatedAtUtc = DateTime.UtcNow.ToString("O")
        });

        await connection.ExecuteAsync(upsert, rows, tx);
        tx.Commit();
        _logger.LogInformation("Upserted movies into app database");
    }

    public async Task<List<PlexMovie>> GetMoviesAsync()
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        const string query = @"
            SELECT
                Id,
                Title,
                OriginalTitle,
                FilePath,
                Year,
                Summary,
                Rating,
                Duration,
                Genres,
                TmdbId,
                TmdbPosterUrl,
                TmdbRating,
                TmdbOverview
            FROM movies
            ORDER BY Title;";

        var movies = (await connection.QueryAsync<PlexMovie>(query)).ToList();
        _logger.LogInformation("Read {Count} movies from app database", movies.Count);
        return movies;
    }

    public async Task<bool> SetMustDeleteAsync(int id, bool mustDelete)
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        const string updateSql = @"
            UPDATE movies
            SET MustDelete = @MustDelete,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;";

        var affectedRows = await connection.ExecuteAsync(updateSql, new
        {
            Id = id,
            MustDelete = mustDelete ? 1 : 0,
            UpdatedAtUtc = DateTime.UtcNow.ToString("O")
        });

        _logger.LogInformation("Updated MustDelete for movie {MovieId}: {MustDelete}", id, mustDelete);
        return affectedRows > 0;
    }

    public async Task<List<PlexMovie>> GetMustDeleteMoviesAsync()
    {
        using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        const string query = @"
            SELECT
                Id,
                Title,
                OriginalTitle,
                FilePath,
                Year,
                Summary,
                Rating,
                Duration,
                Genres,
                TmdbId,
                TmdbPosterUrl,
                TmdbRating,
                TmdbOverview
            FROM movies
            WHERE MustDelete = 1
            ORDER BY Title;";

        var movies = (await connection.QueryAsync<PlexMovie>(query)).ToList();
        _logger.LogInformation("Read {Count} movies marked as MustDelete", movies.Count);
        return movies;
    }
}
