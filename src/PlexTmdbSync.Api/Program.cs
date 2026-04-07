using PlexTmdbSync.Core;
using PlexTmdbSync.Types;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Microsoft.OpenApi.Models;

LoadEnvFile(".env");

var builder = WebApplication.CreateBuilder(args);
var allowedWebClientOrigins = GetConfiguredWebClientOrigins();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateLogger();

builder.Host.UseSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Plex TMDB Sync API",
        Version = "v1",
        Description = "REST API for migrating, syncing, exporting, and searching Plex movie metadata enriched with TMDB data."
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebClient", policy =>
    {
        policy
            .SetIsOriginAllowed(origin => IsAllowedWebClientOrigin(origin, allowedWebClientOrigins))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddPlexTmdbCore();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);

        if (context.Response.HasStarted)
            throw;

        var problem = CreateProblem(
            context,
            StatusCodes.Status500InternalServerError,
            "Unexpected server error",
            "The server failed to process the request.");
        await Results.Json(problem, statusCode: problem.Status, contentType: "application/problem+json").ExecuteAsync(context);
    }
});

app.UseCors("WebClient");

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Plex TMDB Sync API v1");
    options.RoutePrefix = "swagger";
});

app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html"));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck")
    .WithSummary("Returns API health status")
    .WithDescription("Simple health-check endpoint used to verify that the API is running.")
    .Produces(StatusCodes.Status200OK);

app.MapPost("/migrate", async (MovieSyncService syncService) =>
{
    await syncService.MigrateOnlyAsync();
    return Results.Ok(new { message = "Migrations completed" });
})
    .WithName("RunMigrations")
    .WithSummary("Runs app database migrations")
    .WithDescription("Initializes the application database and applies any pending schema migrations.")
    .Produces(StatusCodes.Status200OK);

app.MapPost("/sync", async (HttpContext httpContext, IServiceProvider serviceProvider, SyncRequest request) =>
{
    var batchSize = request.BatchSize ?? 10;
    if (batchSize <= 0 || batchSize > 1000)
        return ValidationProblem(httpContext, "Invalid sync request", ("batchSize", "BatchSize must be between 1 and 1000."));

    var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
        ? "assets/csv/plex_movies.csv"
        : request.OutputPath;
    
    if (outputPath.Length > 1024)
        return ValidationProblem(httpContext, "Invalid sync request", ("outputPath", "OutputPath must not exceed 1024 characters."));

    var syncService = serviceProvider.GetRequiredService<MovieSyncService>();

    var movies = await syncService.RunSyncAsync(
        request.TmdbEnrich ?? true,
        request.ImdbEnrich ?? false,
        batchSize,
        outputPath);

    return Results.Ok(new
    {
        message = "Sync completed",
        count = movies.Count,
        outputPath
    });
})
    .WithName("RunSync")
    .WithSummary("Runs Plex to app-database sync")
    .WithDescription("Reads Plex metadata from the Plex database, optionally enriches movies from TMDB and IMDB APIs, stores them in the app database, and exports a CSV file. Returns sync statistics and output path.")
    .Produces(StatusCodes.Status200OK)
    .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

app.MapGet("/movies", async (AppDatabaseService appDbService) =>
{
    await appDbService.InitializeAsync();
    var movies = await appDbService.GetMoviesAsync();
    return Results.Ok(movies);
})
    .WithName("GetMovies")
    .WithSummary("Returns movies from the app database")
    .WithDescription("Reads all movies from the application database after ensuring the schema is initialized.")
    .Produces(StatusCodes.Status200OK);

app.MapGet("/search", async (
    HttpContext httpContext,
    MovieSearchService searchService,
    string term,
    string? outputPath,
    int? threshold,
    bool? extended) =>
{
    if (string.IsNullOrWhiteSpace(term))
        return ValidationProblem(httpContext, "Invalid search request", ("term", "Search term is required."));
    
    if (term.Length > 256)
        return ValidationProblem(httpContext, "Invalid search request", ("term", "Search term must not exceed 256 characters."));
    
    var thresholdValue = threshold ?? 60;
    if (thresholdValue < 0 || thresholdValue > 100)
        return ValidationProblem(httpContext, "Invalid search request", ("threshold", "Threshold must be between 0 and 100."));

    var csvPath = string.IsNullOrWhiteSpace(outputPath) ? "assets/csv/plex_movies.csv" : outputPath;
    if (csvPath.Length > 1024)
        return ValidationProblem(httpContext, "Invalid search request", ("outputPath", "OutputPath must not exceed 1024 characters."));

    if (!File.Exists(csvPath))
        return ProblemResult(httpContext, StatusCodes.Status404NotFound, "Search source not found", $"CSV file was not found at '{csvPath}'.");

    var results = await searchService.SearchInCsvAsync(
        csvPath,
        term,
        thresholdValue,
        extended ?? false);

    return Results.Ok(results.Select(r => new
    {
        r.Score,
        Movie = r.Movie,
        TmdbUrl = r.Movie.TmdbId > 0 ? $"https://www.themoviedb.org/movie/{r.Movie.TmdbId}" : null,
        ImdbUrl = !string.IsNullOrWhiteSpace(r.Movie.ImdbId) ? $"https://www.imdb.com/title/{r.Movie.ImdbId}/" : null
    }));
})
    .WithName("SearchMovies")
    .WithSummary("Searches generated CSV content")
    .WithDescription("Performs fuzzy movie search against the exported CSV file. Query parameters: term (required, string), outputPath (optional, string), threshold (optional, 0-100, default 60), extended (optional, boolean for extended search mode).")
    .Produces(StatusCodes.Status200OK)
    .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

app.MapPost("/movies/{id:int}/must-delete", async (HttpContext httpContext, AppDatabaseService appDbService, int id) =>
{
    if (id <= 0)
        return ValidationProblem(httpContext, "Invalid movie id", ("id", "Movie id must be greater than 0."));

    await appDbService.InitializeAsync();
    var updated = await appDbService.SetMustDeleteAsync(id, true);

    if (!updated)
        return ProblemResult(httpContext, StatusCodes.Status404NotFound, "Movie not found", $"Movie with id {id} was not found.");

    return Results.Ok(new
    {
        message = "Movie marked as MustDelete",
        id,
        mustDelete = true
    });
})
    .WithName("MarkMovieMustDelete")
    .WithSummary("Marks a movie for deletion")
    .WithDescription("Sets the MustDelete flag to true for the specified movie ID in the application database.")
    .Produces(StatusCodes.Status200OK)
    .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

app.MapGet("/movies/must-delete", async (AppDatabaseService appDbService) =>
{
    await appDbService.InitializeAsync();
    var movies = await appDbService.GetMustDeleteMoviesAsync();
    return Results.Ok(movies);
})
    .WithName("GetMustDeleteMovies")
    .WithSummary("Returns movies marked for deletion")
    .WithDescription("Reads movies from the application database where MustDelete is true.")
    .Produces(StatusCodes.Status200OK);

app.Run();

static void LoadEnvFile(string envPath)
{
    var resolvedEnvPath = ResolveEnvPath(envPath);
    if (resolvedEnvPath is null)
        return;

    var envLines = File.ReadAllLines(resolvedEnvPath);
    foreach (var line in envLines)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim().Trim().Trim('\'', '"'));
        }
    }
}

static string? ResolveEnvPath(string envPath)
{
    var currentDirectory = Directory.GetCurrentDirectory();

    while (!string.IsNullOrWhiteSpace(currentDirectory))
    {
        var candidate = Path.Combine(currentDirectory, envPath);
        if (File.Exists(candidate))
            return candidate;

        var parent = Directory.GetParent(currentDirectory);
        if (parent is null)
            return null;

        currentDirectory = parent.FullName;
    }

    return null;
}

static HashSet<string> GetConfiguredWebClientOrigins()
{
    var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AddOrigins(origins, Environment.GetEnvironmentVariable("WEB_CLIENT_ORIGIN"));
    AddOrigins(origins, Environment.GetEnvironmentVariable("WEB_CLIENT_ORIGINS"));
    return origins;
}

static void AddOrigins(HashSet<string> origins, string? rawOrigins)
{
    if (string.IsNullOrWhiteSpace(rawOrigins))
        return;

    foreach (var origin in rawOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme) && !string.IsNullOrWhiteSpace(uri.Host))
            origins.Add(uri.GetLeftPart(UriPartial.Authority));
    }
}

static bool IsAllowedWebClientOrigin(string origin, HashSet<string> configuredOrigins)
{
    if (configuredOrigins.Contains(origin))
        return true;

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        return false;

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        return false;

    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
}

static IResult ValidationProblem(HttpContext httpContext, string title, params (string Key, string Message)[] errors)
{
    var errorMap = errors
        .GroupBy(error => error.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.Select(error => error.Message).ToArray(),
            StringComparer.OrdinalIgnoreCase);

    return ProblemResult(
        httpContext,
        StatusCodes.Status400BadRequest,
        title,
        "One or more validation errors occurred.",
        errorMap);
}

static IResult ProblemResult(
    HttpContext httpContext,
    int statusCode,
    string title,
    string detail,
    Dictionary<string, string[]>? errors = null)
{
    var problem = CreateProblem(httpContext, statusCode, title, detail, errors);
    return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
}

static ApiProblemDetails CreateProblem(
    HttpContext httpContext,
    int statusCode,
    string title,
    string detail,
    Dictionary<string, string[]>? errors = null)
{
    return new ApiProblemDetails(
        Title: title,
        Status: statusCode,
        Detail: detail,
        Instance: httpContext.Request.Path,
        TraceId: httpContext.TraceIdentifier,
        Errors: errors);
}

public sealed record SyncRequest(bool? TmdbEnrich, bool? ImdbEnrich, int? BatchSize, string? OutputPath);
