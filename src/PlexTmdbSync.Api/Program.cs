using PlexTmdbSync.Core;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Microsoft.OpenApi.Models;

LoadEnvFile(".env");

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddPlexTmdbCore();

var app = builder.Build();

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

app.MapPost("/sync", async (MovieSyncService syncService, SyncRequest request) =>
{
    // Validate batch size
    var batchSize = request.BatchSize ?? 10;
    if (batchSize <= 0 || batchSize > 1000)
        return Results.BadRequest(new { error = "BatchSize must be between 1 and 1000" });

    var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
        ? "assets/csv/plex_movies.csv"
        : request.OutputPath;
    
    // Validate output path length
    if (outputPath.Length > 1024)
        return Results.BadRequest(new { error = "OutputPath must not exceed 1024 characters" });

    var movies = await syncService.RunSyncAsync(
        request.TmdbEnrich ?? true,
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
    .WithDescription("Reads Plex metadata from the Plex database, optionally enriches movies from TMDB API, stores them in the app database, and exports a CSV file. Returns sync statistics and output path.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status500InternalServerError);

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
    MovieSearchService searchService,
    string term,
    string? outputPath,
    int? threshold,
    bool? extended) =>
{
    // Validate required term
    if (string.IsNullOrWhiteSpace(term))
        return Results.BadRequest(new { error = "Search term is required" });
    
    if (term.Length > 256)
        return Results.BadRequest(new { error = "Search term must not exceed 256 characters" });
    
    // Validate threshold range
    var thresholdValue = threshold ?? 60;
    if (thresholdValue < 0 || thresholdValue > 100)
        return Results.BadRequest(new { error = "Threshold must be between 0 and 100" });

    var csvPath = string.IsNullOrWhiteSpace(outputPath) ? "assets/csv/plex_movies.csv" : outputPath;
    var results = await searchService.SearchInCsvAsync(
        csvPath,
        term,
        thresholdValue,
        extended ?? false);

    return Results.Ok(results.Select(r => new
    {
        r.Score,
        Movie = r.Movie,
        TmdbUrl = r.Movie.TmdbId > 0 ? $"https://www.themoviedb.org/movie/{r.Movie.TmdbId}" : null
    }));
})
    .WithName("SearchMovies")
    .WithSummary("Searches generated CSV content")
    .WithDescription("Performs fuzzy movie search against the exported CSV file. Query parameters: term (required, string), outputPath (optional, string), threshold (optional, 0-100, default 60), extended (optional, boolean for extended search mode).")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

app.MapPost("/movies/{id:int}/must-delete", async (AppDatabaseService appDbService, int id) =>
{
    if (id <= 0)
        return Results.BadRequest("Movie id must be greater than 0.");

    await appDbService.InitializeAsync();
    var updated = await appDbService.SetMustDeleteAsync(id, true);

    if (!updated)
        return Results.NotFound();

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
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound);

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
    if (!File.Exists(envPath))
        return;

    var envLines = File.ReadAllLines(envPath);
    foreach (var line in envLines)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim().Trim('\''));
        }
    }
}

public sealed record SyncRequest(bool? TmdbEnrich, int? BatchSize, string? OutputPath);
