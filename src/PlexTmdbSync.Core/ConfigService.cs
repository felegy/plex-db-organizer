namespace PlexTmdbSync.Core;

public class ConfigService
{
    public string PlexDatabasePath { get; }
    public string AppDatabaseConnectionString { get; }
    public string TmdbApiKey { get; }
    public string TmdbApiBaseUrl { get; }
    public string TmdbAccessToken { get; }

    public ConfigService()
    {
        PlexDatabasePath = Environment.GetEnvironmentVariable("PLEX_DATABASE_PATH") ?? "assets/db/com.plexapp.plugins.library.db";
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") ?? "sqlite://assets/db/plex_movies_app.db";
        AppDatabaseConnectionString = BuildSqliteConnectionString(databaseUrl);
        TmdbApiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? throw new InvalidOperationException("TMDB_API_KEY not found in environment");
        TmdbApiBaseUrl = Environment.GetEnvironmentVariable("TMDB_API_BASE_URL") ?? "https://api.themoviedb.org/3";
        TmdbAccessToken = Environment.GetEnvironmentVariable("TMDB_ACCESS_TOKEN") ?? throw new InvalidOperationException("TMDB_ACCESS_TOKEN not found in environment");
    }

    private static string BuildSqliteConnectionString(string databaseUrl)
    {
        if (databaseUrl.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            return databaseUrl;

        string dbPath;
        if (databaseUrl.StartsWith("sqlite:///", StringComparison.OrdinalIgnoreCase))
        {
            dbPath = "/" + databaseUrl["sqlite:///".Length..].TrimStart('/');
        }
        else if (databaseUrl.StartsWith("sqlite://", StringComparison.OrdinalIgnoreCase))
        {
            dbPath = databaseUrl["sqlite://".Length..];
        }
        else
        {
            dbPath = databaseUrl;
        }

        var resolvedPath = Path.IsPathRooted(dbPath)
            ? dbPath
            : Path.Combine(Directory.GetCurrentDirectory(), dbPath);

        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={resolvedPath};Version=3;";
    }
}
