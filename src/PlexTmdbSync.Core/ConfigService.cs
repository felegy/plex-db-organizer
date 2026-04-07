namespace PlexTmdbSync.Core;

public class ConfigService
{
    public string PlexDatabasePath { get; }
    public string AppDatabaseConnectionString { get; }
    public string? TmdbApiKey { get; }
    public string TmdbApiBaseUrl { get; }
    public string? TmdbAccessToken { get; }
    public string? ImdbApiKey { get; }
    public string ImdbApiBaseUrl { get; }
    public string? RapidApiKey { get; }
    public string? RapidApiHost { get; }
    public int ImdbMaxParallelism { get; }
    public int ImdbMaxRetryAttempts { get; }

    public ConfigService()
    {
        PlexDatabasePath = Environment.GetEnvironmentVariable("PLEX_DATABASE_PATH") ?? "assets/db/com.plexapp.plugins.library.db";
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") ?? "sqlite://assets/db/plex_movies_app.db";
        AppDatabaseConnectionString = BuildSqliteConnectionString(databaseUrl);
        TmdbApiKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");
        TmdbApiBaseUrl = Environment.GetEnvironmentVariable("TMDB_API_BASE_URL") ?? "https://api.themoviedb.org/3";
        TmdbAccessToken = Environment.GetEnvironmentVariable("TMDB_ACCESS_TOKEN");
        ImdbApiKey = Environment.GetEnvironmentVariable("IMDB_API_KEY");
        ImdbApiBaseUrl = NormalizeBaseUrl(Environment.GetEnvironmentVariable("IMDB_API_BASE_URL") ?? "https://www.omdbapi.com");
        RapidApiKey = Environment.GetEnvironmentVariable("RAPIDAPI_KEY")
            ?? Environment.GetEnvironmentVariable("X_RAPIDAPI_KEY");
        RapidApiHost = Environment.GetEnvironmentVariable("RAPIDAPI_HOST")
            ?? Environment.GetEnvironmentVariable("X_RAPIDAPI_HOST");
        ImdbMaxParallelism = ParsePositiveInt(Environment.GetEnvironmentVariable("IMDB_MAX_PARALLELISM"), 3);
        ImdbMaxRetryAttempts = ParsePositiveInt(Environment.GetEnvironmentVariable("IMDB_MAX_RETRY_ATTEMPTS"), 4);
    }

    public string RequireTmdbApiKey()
    {
        return TmdbApiKey ?? throw new InvalidOperationException("TMDB_API_KEY not found in environment");
    }

    public string RequireTmdbAccessToken()
    {
        return TmdbAccessToken ?? throw new InvalidOperationException("TMDB_ACCESS_TOKEN not found in environment");
    }

    public string RequireImdbApiKey()
    {
        return ImdbApiKey
            ?? RapidApiKey
            ?? throw new InvalidOperationException("IMDB_API_KEY or RAPIDAPI_KEY not found in environment");
    }

    public bool UseRapidApiForImdb()
    {
        if (!string.IsNullOrWhiteSpace(RapidApiHost) || !string.IsNullOrWhiteSpace(RapidApiKey))
            return true;

        if (Uri.TryCreate(ImdbApiBaseUrl, UriKind.Absolute, out var uri))
            return uri.Host.Contains("rapidapi.com", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public string? GetRapidApiHost()
    {
        if (!string.IsNullOrWhiteSpace(RapidApiHost))
            return RapidApiHost;

        if (Uri.TryCreate(ImdbApiBaseUrl, UriKind.Absolute, out var uri))
            return uri.Host;

        return null;
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

    private static string NormalizeBaseUrl(string rawBaseUrl)
    {
        var trimmed = rawBaseUrl.Trim().Trim('"', '\'').TrimEnd('/');
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"https://{trimmed}";
    }

    private static int ParsePositiveInt(string? value, int fallback)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
            return parsed;

        return fallback;
    }
}
